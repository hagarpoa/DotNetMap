using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Store;

namespace DotNetMap.Core.Extraction;

/// <summary>
/// On-demand reference sites for methods, properties, fields, and events (DNM-005 + DNM-012).
/// One <see cref="RelationRef"/> per site with file + line.
/// </summary>
public sealed class MemberReferencesExtractor
{
    public const int MaxSites = 50;

    public async Task<IReadOnlyList<RelationRef>> FindReferenceSitesAsync(
        Solution solution,
        ISymbol symbol,
        CancellationToken cancellationToken = default)
    {
        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken)
            .ConfigureAwait(false);

        var solutionDir = GuessSolutionDir(solution);
        var sites = new List<RelationRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var referenced in refs)
        {
            foreach (var location in referenced.Locations)
            {
                if (!location.Location.IsInSource || location.Document is null)
                    continue;

                var doc = location.Document;
                var model = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (model is null || root is null)
                    continue;

                var node = root.FindNode(location.Location.SourceSpan);
                var container = ResolveContainerSymbol(model, node, cancellationToken);
                if (container is null)
                    continue;

                // Skip definition of the same symbol
                if (SymbolEqualityComparer.Default.Equals(container.OriginalDefinition, symbol.OriginalDefinition))
                    continue;

                var (id, display) = ToIdAndDisplay(container);
                var lineSpan = location.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;
                var rel = ToRelative(solutionDir, doc.FilePath);

                var siteKey = $"{id}|{rel}|{line}";
                if (!seen.Add(siteKey))
                    continue;

                sites.Add(new RelationRef(
                    RelationKind.ReferencedBy,
                    id,
                    display,
                    File: rel,
                    Line: line));

                if (sites.Count >= MaxSites)
                    return OrderSites(sites);
            }
        }

        return OrderSites(sites);
    }

    public static async Task<ISymbol?> FindMemberSymbolAsync(
        Solution solution,
        MemberDetail member,
        CancellationToken cancellationToken = default)
    {
        var kind = member.Kind.ToLowerInvariant();
        return kind switch
        {
            "method" or "constructor" =>
                await MethodCallersExtractor.FindMethodSymbolAsync(
                    solution, member.Id, member.ParentTypeFullName, cancellationToken).ConfigureAwait(false)
                ?? await MethodCallersExtractor.FindMethodSymbolAsync(
                    solution, member.Name, member.ParentTypeFullName, cancellationToken).ConfigureAwait(false),
            "property" =>
                await FindPropertySymbolAsync(solution, member, cancellationToken).ConfigureAwait(false),
            "field" =>
                await FindFieldSymbolAsync(solution, member, cancellationToken).ConfigureAwait(false),
            "event" =>
                await FindEventSymbolAsync(solution, member, cancellationToken).ConfigureAwait(false),
            _ => await FindAnyMemberSymbolAsync(solution, member, cancellationToken).ConfigureAwait(false)
        };
    }

    private static async Task<ISymbol?> FindAnyMemberSymbolAsync(
        Solution solution,
        MemberDetail member,
        CancellationToken cancellationToken)
    {
        ISymbol? s = await MethodCallersExtractor.FindMethodSymbolAsync(
            solution, member.Name, member.ParentTypeFullName, cancellationToken).ConfigureAwait(false);
        s ??= await FindPropertySymbolAsync(solution, member, cancellationToken).ConfigureAwait(false);
        s ??= await FindFieldSymbolAsync(solution, member, cancellationToken).ConfigureAwait(false);
        s ??= await FindEventSymbolAsync(solution, member, cancellationToken).ConfigureAwait(false);
        return s;
    }

    private static async Task<IPropertySymbol?> FindPropertySymbolAsync(
        Solution solution,
        MemberDetail member,
        CancellationToken cancellationToken)
    {
        foreach (var type in await EnumerateTypesAsync(solution, member.ParentTypeFullName, cancellationToken)
                     .ConfigureAwait(false))
        {
            var prop = type.GetMembers(member.Name).OfType<IPropertySymbol>()
                .FirstOrDefault(p => !p.IsImplicitlyDeclared);
            if (prop is not null)
                return prop;
        }

        return null;
    }

    private static async Task<IFieldSymbol?> FindFieldSymbolAsync(
        Solution solution,
        MemberDetail member,
        CancellationToken cancellationToken)
    {
        foreach (var type in await EnumerateTypesAsync(solution, member.ParentTypeFullName, cancellationToken)
                     .ConfigureAwait(false))
        {
            var field = type.GetMembers(member.Name).OfType<IFieldSymbol>()
                .FirstOrDefault(f => !f.IsImplicitlyDeclared && f.AssociatedSymbol is null);
            if (field is not null)
                return field;
        }

        return null;
    }

    private static async Task<IEventSymbol?> FindEventSymbolAsync(
        Solution solution,
        MemberDetail member,
        CancellationToken cancellationToken)
    {
        foreach (var type in await EnumerateTypesAsync(solution, member.ParentTypeFullName, cancellationToken)
                     .ConfigureAwait(false))
        {
            var ev = type.GetMembers(member.Name).OfType<IEventSymbol>().FirstOrDefault();
            if (ev is not null)
                return ev;
        }

        return null;
    }

    private static async Task<IEnumerable<INamedTypeSymbol>> EnumerateTypesAsync(
        Solution solution,
        string? parentTypeFullName,
        CancellationToken cancellationToken)
    {
        var list = new List<INamedTypeSymbol>();
        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            if (!string.IsNullOrEmpty(parentTypeFullName))
            {
                var t = compilation.GetTypeByMetadataName(parentTypeFullName)
                        ?? compilation.GetTypeByMetadataName(parentTypeFullName.Replace('.', '+'));
                if (t is not null)
                    list.Add(t);
            }
        }

        return list;
    }

    private static ISymbol? ResolveContainerSymbol(
        SemanticModel model,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl is not null)
            return model.GetDeclaredSymbol(methodDecl, cancellationToken);

        var ctor = node.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor is not null)
            return model.GetDeclaredSymbol(ctor, cancellationToken);

        var local = node.AncestorsAndSelf().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
        if (local is not null)
            return model.GetDeclaredSymbol(local, cancellationToken);

        var prop = node.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (prop is not null)
            return model.GetDeclaredSymbol(prop, cancellationToken);

        var enclosing = model.GetEnclosingSymbol(node.SpanStart, cancellationToken);
        while (enclosing is not null)
        {
            if (enclosing is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
                return enclosing;
            if (enclosing is INamedTypeSymbol type)
                return type;
            enclosing = enclosing.ContainingSymbol;
        }

        return null;
    }

    private static (string Id, string Display) ToIdAndDisplay(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol m => (
                Ids.Method(m.OriginalDefinition.ToDisplayString(MetadataFormat)),
                m.ToDisplayString(DisplayFormat)),
            IPropertySymbol p => (
                Ids.Property(p.OriginalDefinition.ToDisplayString(MetadataFormat)),
                p.ToDisplayString(DisplayFormat)),
            IFieldSymbol f => (
                Ids.Field(f.OriginalDefinition.ToDisplayString(MetadataFormat)),
                f.ToDisplayString(DisplayFormat)),
            IEventSymbol e => (
                Ids.Event(e.OriginalDefinition.ToDisplayString(MetadataFormat)),
                e.ToDisplayString(DisplayFormat)),
            INamedTypeSymbol t => (
                Ids.Type(t.ToDisplayString(new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters))),
                t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "")),
            _ => (
                "symbol:" + symbol.ToDisplayString(MetadataFormat),
                symbol.ToDisplayString(DisplayFormat))
        };
    }

    private static IReadOnlyList<RelationRef> OrderSites(List<RelationRef> sites) =>
        sites
            .OrderBy(s => s.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Line)
            .ToList();

    private static string? GuessSolutionDir(Solution solution)
    {
        var path = solution.FilePath;
        if (!string.IsNullOrEmpty(path))
            return Path.GetDirectoryName(Path.GetFullPath(path));
        var first = solution.Projects.Select(p => p.FilePath).FirstOrDefault(f => !string.IsNullOrEmpty(f));
        return first is null ? null : Path.GetDirectoryName(Path.GetFullPath(first));
    }

    private static string? ToRelative(string? solutionDir, string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return null;
        try
        {
            if (!string.IsNullOrEmpty(solutionDir))
                return Path.GetRelativePath(solutionDir, absolutePath).Replace('\\', '/');
        }
        catch
        {
            // ignore
        }

        return Path.GetFileName(absolutePath);
    }

    private static readonly SymbolDisplayFormat MetadataFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None);

    private static readonly SymbolDisplayFormat DisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType
                       | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
}
