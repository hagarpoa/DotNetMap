using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Extraction;

/// <summary>
/// Scoped consumer discovery via SymbolFinder.FindReferencesAsync.
/// Never runs solution-wide unless scope is explicitly "full".
/// </summary>
public sealed class ConsumersExtractor
{
    public const int MaxConsumersPerType = 100;

    private readonly IProgress<string>? _progress;

    public ConsumersExtractor(IProgress<string>? progress = null) => _progress = progress;

    public async Task ApplyAsync(
        SolutionMap map,
        Solution solution,
        IReadOnlyList<RelationScope> scopes,
        CancellationToken cancellationToken = default)
    {
        if (scopes.Count == 0)
            return;

        var targets = ResolveTargetTypes(map, scopes).ToList();
        if (targets.Count == 0)
        {
            _progress?.Report("relations: no matching types for given scopes");
            return;
        }

        _progress?.Report($"relations: finding consumers for {targets.Count} type(s)...");

        // Build lookup of compilations / symbols by metadata-ish name
        var projectCompilations = new Dictionary<ProjectId, Compilation?>();
        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            projectCompilations[project.Id] = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        }

        var typeByFullName = map.Projects
            .SelectMany(p => p.Types)
            .GroupBy(t => t.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var updated = 0;
        foreach (var typeNode in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = FindNamedTypeSymbol(projectCompilations, typeNode.FullName, typeNode.Name);
            if (symbol is null)
            {
                _progress?.Report($"  skip (symbol not found): {typeNode.FullName}");
                continue;
            }

            var consumers = await FindConsumerTypesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
            typeNode.Consumers.Clear();
            foreach (var c in consumers.Take(MaxConsumersPerType))
                typeNode.Consumers.Add(c);

            updated++;
            _progress?.Report($"  {typeNode.FullName}: {typeNode.Consumers.Count} consumer(s)");
        }

        if (scopes.Any(s => s.Kind == RelationScopeKind.Full) || scopes.Count > 0 && updated > 0)
        {
            // Elevate mode only when we actually ran consumer discovery
            if (map.Mode != IndexMode.FullRelations && scopes.Any(s => s.Kind == RelationScopeKind.Full))
            {
                // map.Mode is init-only — handled via reassignment at caller if needed
            }
        }

        _progress?.Report($"relations: updated consumers on {updated} type(s)");
    }

    private static IEnumerable<TypeNode> ResolveTargetTypes(SolutionMap map, IReadOnlyList<RelationScope> scopes)
    {
        if (scopes.Any(s => s.Kind == RelationScopeKind.Full))
            return map.Projects.SelectMany(p => p.Types);

        var set = new Dictionary<string, TypeNode>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            switch (scope.Kind)
            {
                case RelationScopeKind.Type:
                    foreach (var t in map.Projects.SelectMany(p => p.Types))
                    {
                        if (t.FullName.Equals(scope.Name, StringComparison.OrdinalIgnoreCase)
                            || t.Name.Equals(scope.Name, StringComparison.OrdinalIgnoreCase)
                            || t.Id.Equals(scope.Name, StringComparison.OrdinalIgnoreCase)
                            || t.Id.Equals("type:" + scope.Name, StringComparison.OrdinalIgnoreCase)
                            || t.FullName.EndsWith("." + scope.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            set[t.Id] = t;
                        }
                    }
                    break;

                case RelationScopeKind.Project:
                    foreach (var p in map.Projects)
                    {
                        if (p.Name.Equals(scope.Name, StringComparison.OrdinalIgnoreCase)
                            || p.Id.Equals(scope.Name, StringComparison.OrdinalIgnoreCase)
                            || p.Id.Equals("project:" + scope.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var t in p.Types)
                                set[t.Id] = t;
                        }
                    }
                    break;
            }
        }

        return set.Values;
    }

    private static INamedTypeSymbol? FindNamedTypeSymbol(
        Dictionary<ProjectId, Compilation?> compilations,
        string fullName,
        string shortName)
    {
        foreach (var compilation in compilations.Values)
        {
            if (compilation is null)
                continue;

            // Try metadata name style and dotted full name
            var symbol = compilation.GetTypeByMetadataName(fullName)
                         ?? compilation.GetTypeByMetadataName(fullName.Replace('.', '+'));
            if (symbol is not null)
                return symbol;

            // Fallback: scan global namespace tree (small solutions)
            var candidates = compilation.GetSymbolsWithName(shortName, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .Where(s =>
                {
                    var display = s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        .Replace("global::", "", StringComparison.Ordinal);
                    return display.Equals(fullName, StringComparison.Ordinal)
                           || s.Name.Equals(shortName, StringComparison.Ordinal);
                })
                .ToList();

            if (candidates.Count == 1)
                return candidates[0];
            if (candidates.Count > 1)
            {
                return candidates.FirstOrDefault(s =>
                    s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        .Replace("global::", "", StringComparison.Ordinal)
                        .Equals(fullName, StringComparison.Ordinal)) ?? candidates[0];
            }
        }

        return null;
    }

    private static async Task<List<RelationRef>> FindConsumerTypesAsync(
        INamedTypeSymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
        var consumers = new Dictionary<string, RelationRef>(StringComparer.Ordinal);

        foreach (var referencedSymbol in refs)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                if (!location.Location.IsInSource)
                    continue;

                var doc = location.Document;
                if (doc is null)
                    continue;

                var model = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (model is null || root is null)
                    continue;

                var node = root.FindNode(location.Location.SourceSpan);
                var consumerType = ResolveConsumerType(model, node, cancellationToken);
                if (consumerType is null)
                    continue;

                // Skip self-references / same type
                if (SameType(consumerType, symbol))
                    continue;

                var name = consumerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", "", StringComparison.Ordinal);
                var id = Ids.Type(consumerType.ToDisplayString(new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters)));

                if (consumers.ContainsKey(id))
                    continue;

                consumers[id] = new RelationRef(RelationKind.ReferencedBy, id, name);
            }
        }

        // Also include explicit interface implementors (more reliable than refs alone)
        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            foreach (var impl in FindImplementors(compilation.GlobalNamespace, symbol))
            {
                var name = impl.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", "", StringComparison.Ordinal);
                var id = Ids.Type(impl.ToDisplayString(new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters)));
                consumers[id] = new RelationRef(RelationKind.ReferencedBy, id, name);
            }
        }

        return consumers.Values
            .OrderBy(c => c.TargetName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static INamedTypeSymbol? ResolveConsumerType(
        SemanticModel model,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        // Prefer the containing type declaration (works for base lists, attributes, etc.)
        var typeDecl = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is not null)
        {
            if (model.GetDeclaredSymbol(typeDecl, cancellationToken) is INamedTypeSymbol declared)
                return declared;
        }

        var enclosing = model.GetEnclosingSymbol(node.SpanStart, cancellationToken);
        return enclosing as INamedTypeSymbol ?? enclosing?.ContainingType as INamedTypeSymbol
               ?? enclosing?.ContainingType;
    }

    private static IEnumerable<INamedTypeSymbol> FindImplementors(
        INamespaceSymbol ns,
        INamedTypeSymbol targetInterface)
    {
        if (targetInterface.TypeKind != Microsoft.CodeAnalysis.TypeKind.Interface)
            yield break;

        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    foreach (var impl in FindImplementors(childNs, targetInterface))
                        yield return impl;
                    break;
                case INamedTypeSymbol type:
                    foreach (var impl in FindImplementorsInType(type, targetInterface))
                        yield return impl;
                    break;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> FindImplementorsInType(
        INamedTypeSymbol type,
        INamedTypeSymbol targetInterface)
    {
        if (type.AllInterfaces.Any(i => SameType(i, targetInterface)))
        {
            yield return type;
        }

        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var impl in FindImplementorsInType(nested, targetInterface))
                yield return impl;
        }
    }

    private static bool SameType(INamedTypeSymbol a, INamedTypeSymbol b)
    {
        if (SymbolEqualityComparer.Default.Equals(a, b))
            return true;
        if (SymbolEqualityComparer.Default.Equals(a.OriginalDefinition, b.OriginalDefinition))
            return true;

        // Cross-compilation / metadata vs source
        var an = a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var bn = b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(an, bn, StringComparison.Ordinal);
    }
}
