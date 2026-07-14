using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Extraction;

/// <summary>
/// On-demand method callers via SymbolFinder (scoped — never full solution default).
/// Returns one entry per call site with file + line (DNM-005).
/// </summary>
public sealed class MethodCallersExtractor
{
    /// <summary>Max reference sites returned (not unique callers).</summary>
    public const int MaxCallers = 50;

    public async Task<IReadOnlyList<RelationRef>> FindCallersAsync(
        Solution solution,
        IMethodSymbol method,
        CancellationToken cancellationToken = default)
    {
        var refs = await SymbolFinder.FindReferencesAsync(method, solution, cancellationToken)
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
                var caller = ResolveCallerMethod(model, node, cancellationToken);
                if (caller is null)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(caller.OriginalDefinition, method.OriginalDefinition))
                    continue;

                var idName = caller.OriginalDefinition.ToDisplayString(MetadataFormat);
                var id = Ids.Method(idName);
                var display = caller.ToDisplayString(DisplayFormat);

                var lineSpan = location.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;
                var filePath = doc.FilePath;
                var rel = ToRelative(solutionDir, filePath);

                // Dedupe identical site
                var siteKey = $"{id}|{rel}|{line}";
                if (!seen.Add(siteKey))
                    continue;

                sites.Add(new RelationRef(
                    RelationKind.ReferencedBy,
                    id,
                    display,
                    File: rel,
                    Line: line));

                if (sites.Count >= MaxCallers)
                    return OrderSites(sites);
            }
        }

        return OrderSites(sites);
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
            // fall through
        }

        return Path.GetFileName(absolutePath);
    }

    /// <summary>Find IMethodSymbol in the loaded solution matching a stored member id/name.</summary>
    public static async Task<IMethodSymbol?> FindMethodSymbolAsync(
        Solution solution,
        string nameOrId,
        string? parentTypeFullName,
        CancellationToken cancellationToken = default)
    {
        var shortName = nameOrId;
        if (shortName.StartsWith("method:", StringComparison.OrdinalIgnoreCase))
            shortName = shortName["method:".Length..];

        string? typeHint = parentTypeFullName;

        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            if (!string.IsNullOrEmpty(typeHint))
            {
                var type = compilation.GetTypeByMetadataName(typeHint)
                           ?? compilation.GetTypeByMetadataName(typeHint.Replace('.', '+'));
                if (type is not null)
                {
                    var methodName = shortName.Contains('.') ? shortName[(shortName.LastIndexOf('.') + 1)..] : shortName;
                    methodName = methodName.Contains('(') ? methodName[..methodName.IndexOf('(')] : methodName;
                    var match = type.GetMembers(methodName).OfType<IMethodSymbol>()
                        .FirstOrDefault(m => !m.IsImplicitlyDeclared
                                             && m.MethodKind is MethodKind.Ordinary or MethodKind.Constructor);
                    if (match is not null)
                        return match;
                }
            }

            var simple = shortName;
            if (simple.Contains('(', StringComparison.Ordinal))
                simple = simple[..simple.IndexOf('(')];
            if (simple.Contains('.'))
                simple = simple[(simple.LastIndexOf('.') + 1)..];

            foreach (var sym in compilation.GetSymbolsWithName(simple, SymbolFilter.Member).OfType<IMethodSymbol>())
            {
                if (sym.IsImplicitlyDeclared)
                    continue;
                var display = sym.ToDisplayString(MetadataFormat);
                var id = Ids.Method(display);
                if (id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)
                    || display.Equals(shortName, StringComparison.OrdinalIgnoreCase)
                    || sym.Name.Equals(simple, StringComparison.OrdinalIgnoreCase))
                {
                    if (typeHint is null
                        || (sym.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                            .Replace("global::", "")
                            .Equals(typeHint, StringComparison.OrdinalIgnoreCase) ?? false))
                        return sym;
                }
            }
        }

        return null;
    }

    private static IMethodSymbol? ResolveCallerMethod(
        SemanticModel model,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl is not null)
            return model.GetDeclaredSymbol(methodDecl, cancellationToken) as IMethodSymbol;

        var ctor = node.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor is not null)
            return model.GetDeclaredSymbol(ctor, cancellationToken) as IMethodSymbol;

        var local = node.AncestorsAndSelf().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
        if (local is not null)
            return model.GetDeclaredSymbol(local, cancellationToken) as IMethodSymbol;

        var enclosing = model.GetEnclosingSymbol(node.SpanStart, cancellationToken);
        while (enclosing is not null)
        {
            if (enclosing is IMethodSymbol ms
                && ms.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.LocalFunction)
                return ms;
            enclosing = enclosing.ContainingSymbol;
        }

        return null;
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
