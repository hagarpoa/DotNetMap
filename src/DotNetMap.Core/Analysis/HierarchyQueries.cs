using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Core.Analysis;

/// <summary>
/// Implementations of interfaces/abstract types and overrides of virtual methods (DNM-003 / DNM-011).
/// </summary>
public static class HierarchyQueries
{
    public const int MaxHits = 50;

    public sealed record TypeHit(
        string TypeId,
        string FullName,
        string Kind,
        string? File,
        int? Line);

    public sealed record MethodHit(
        string MethodId,
        string DisplayName,
        string ContainingType,
        string? File,
        int? Line);

    public sealed record ImplementationsResult(
        string Query,
        string? ResolvedType,
        IReadOnlyList<TypeHit> Hits);

    public sealed record OverridesResult(
        string Query,
        string? ResolvedMethod,
        IReadOnlyList<MethodHit> Hits);

    public static async Task<ImplementationsResult> FindImplementationsAsync(
        MapStore store,
        string typeNameOrId,
        int max = MaxHits,
        CancellationToken cancellationToken = default)
    {
        max = Math.Clamp(max, 1, 100);
        var typeDetail = store.GetTypeDetail(typeNameOrId, maxMembers: 1);
        var status = store.GetStatus();
        if (string.IsNullOrEmpty(status.SolutionPath))
            throw new InvalidOperationException("Solution path missing from index. Re-run index.");

        using var loader = WorkspaceLoader.Create();
        var solution = await loader.OpenAsync(status.SolutionPath, cancellationToken).ConfigureAwait(false);
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(status.SolutionPath));

        var shortName = typeDetail?.FullName is { } fn && fn.Contains('.')
            ? fn[(fn.LastIndexOf('.') + 1)..]
            : typeNameOrId;
        var symbol = await FindTypeSymbolAsync(
            solution,
            typeDetail?.FullName ?? typeNameOrId,
            shortName,
            cancellationToken).ConfigureAwait(false);

        if (symbol is null)
            throw new InvalidOperationException($"Could not resolve type symbol: {typeNameOrId}");

        var hits = new List<TypeHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Roslyn: implementations of interface / abstract type
        var impls = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var impl in impls.OfType<INamedTypeSymbol>())
        {
            if (impl.Locations.All(l => !l.IsInSource))
                continue;
            AddTypeHit(hits, seen, impl, "implements", solutionDir);
            if (hits.Count >= max)
                break;
        }

        // Also derived classes for non-interface types (subtypes)
        if (symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Interface && hits.Count < max)
        {
            var derived = await SymbolFinder.FindDerivedClassesAsync(symbol, solution, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var d in derived)
            {
                if (d.Locations.All(l => !l.IsInSource))
                    continue;
                AddTypeHit(hits, seen, d, "extends", solutionDir);
                if (hits.Count >= max)
                    break;
            }
        }

        var resolved = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "", StringComparison.Ordinal);

        return new ImplementationsResult(
            typeNameOrId,
            resolved,
            hits.OrderBy(h => h.FullName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static async Task<OverridesResult> FindOverridesAsync(
        MapStore store,
        string methodNameOrId,
        int max = MaxHits,
        CancellationToken cancellationToken = default)
    {
        max = Math.Clamp(max, 1, 100);
        var member = store.GetMemberDetail(methodNameOrId);
        var status = store.GetStatus();
        if (string.IsNullOrEmpty(status.SolutionPath))
            throw new InvalidOperationException("Solution path missing from index. Re-run index.");

        using var loader = WorkspaceLoader.Create();
        var solution = await loader.OpenAsync(status.SolutionPath, cancellationToken).ConfigureAwait(false);
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(status.SolutionPath));

        IMethodSymbol? symbol = null;
        if (member is not null)
        {
            symbol = await MethodCallersExtractor.FindMethodSymbolAsync(
                solution, member.Id, member.ParentTypeFullName, cancellationToken).ConfigureAwait(false);
            symbol ??= await MethodCallersExtractor.FindMethodSymbolAsync(
                solution, member.Name, member.ParentTypeFullName, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            symbol = await MethodCallersExtractor.FindMethodSymbolAsync(
                solution, methodNameOrId, null, cancellationToken).ConfigureAwait(false);
        }

        if (symbol is null)
            throw new InvalidOperationException($"Could not resolve method symbol: {methodNameOrId}");

        var hits = new List<MethodHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var overrides = await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var ov in overrides.OfType<IMethodSymbol>())
        {
            if (ov.Locations.All(l => !l.IsInSource))
                continue;
            AddMethodHit(hits, seen, ov, solutionDir);
            if (hits.Count >= max)
                break;
        }

        // Also explicit interface implementations when querying interface method
        if (symbol.ContainingType?.TypeKind == Microsoft.CodeAnalysis.TypeKind.Interface && hits.Count < max)
        {
            var impls = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var impl in impls.OfType<IMethodSymbol>())
            {
                if (impl.Locations.All(l => !l.IsInSource))
                    continue;
                AddMethodHit(hits, seen, impl, solutionDir);
                if (hits.Count >= max)
                    break;
            }
        }

        var resolved = symbol.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            memberOptions: SymbolDisplayMemberOptions.IncludeContainingType
                           | SymbolDisplayMemberOptions.IncludeParameters,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

        return new OverridesResult(
            methodNameOrId,
            resolved,
            hits.OrderBy(h => h.ContainingType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public static string FormatImplementations(ImplementationsResult result, string format = "md")
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                query = result.Query,
                resolvedType = result.ResolvedType,
                count = result.Hits.Count,
                implementations = result.Hits
            }, JsonOpts);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Implementations of {result.ResolvedType ?? result.Query}");
        sb.AppendLine();
        sb.AppendLine($"Count: {result.Hits.Count}");
        sb.AppendLine();
        foreach (var h in result.Hits)
        {
            var site = h.File is null ? "" : h.Line is int l ? $" @ `{h.File}:L{l}`" : $" @ `{h.File}`";
            sb.AppendLine($"- **[{h.Kind}]** `{h.FullName}`{site}");
        }

        sb.AppendLine();
        sb.AppendLine($"_~{TokenEstimator.FromText(sb.ToString())} tokens_");
        return sb.ToString();
    }

    public static string FormatOverrides(OverridesResult result, string format = "md")
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                query = result.Query,
                resolvedMethod = result.ResolvedMethod,
                count = result.Hits.Count,
                overrides = result.Hits
            }, JsonOpts);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Overrides of {result.ResolvedMethod ?? result.Query}");
        sb.AppendLine();
        sb.AppendLine($"Count: {result.Hits.Count}");
        sb.AppendLine();
        foreach (var h in result.Hits)
        {
            var site = h.File is null ? "" : h.Line is int l ? $" @ `{h.File}:L{l}`" : $" @ `{h.File}`";
            sb.AppendLine($"- `{h.ContainingType}.{h.DisplayName}`{site}");
        }

        sb.AppendLine();
        sb.AppendLine($"_~{TokenEstimator.FromText(sb.ToString())} tokens_");
        return sb.ToString();
    }

    private static void AddTypeHit(
        List<TypeHit> hits,
        HashSet<string> seen,
        INamedTypeSymbol type,
        string kind,
        string? solutionDir)
    {
        var full = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "", StringComparison.Ordinal);
        var id = Ids.Type(type.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters)));
        if (!seen.Add(id))
            return;

        var loc = type.Locations.FirstOrDefault(l => l.IsInSource);
        string? file = null;
        int? line = null;
        if (loc is not null)
        {
            var span = loc.GetLineSpan();
            line = span.StartLinePosition.Line + 1;
            file = ToRelative(solutionDir, loc.SourceTree?.FilePath);
        }

        hits.Add(new TypeHit(id, full, kind, file, line));
    }

    private static void AddMethodHit(
        List<MethodHit> hits,
        HashSet<string> seen,
        IMethodSymbol method,
        string? solutionDir)
    {
        var idName = method.OriginalDefinition.ToDisplayString(new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters
                           | SymbolDisplayMemberOptions.IncludeContainingType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType));
        var id = Ids.Method(idName);
        if (!seen.Add(id))
            return;

        var containing = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "", StringComparison.Ordinal) ?? "";
        var display = method.Name;

        var loc = method.Locations.FirstOrDefault(l => l.IsInSource);
        string? file = null;
        int? line = null;
        if (loc is not null)
        {
            var span = loc.GetLineSpan();
            line = span.StartLinePosition.Line + 1;
            file = ToRelative(solutionDir, loc.SourceTree?.FilePath);
        }

        hits.Add(new MethodHit(id, display, containing, file, line));
    }

    private static async Task<INamedTypeSymbol?> FindTypeSymbolAsync(
        Solution solution,
        string fullOrQuery,
        string shortName,
        CancellationToken cancellationToken)
    {
        var query = fullOrQuery.Trim();
        if (query.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            query = query["type:".Length..];

        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                continue;

            var symbol = compilation.GetTypeByMetadataName(query)
                         ?? compilation.GetTypeByMetadataName(query.Replace('.', '+'));
            if (symbol is not null)
                return symbol;

            var simple = shortName.Contains('.') ? shortName[(shortName.LastIndexOf('.') + 1)..] : shortName;
            foreach (var t in compilation.GetSymbolsWithName(simple, SymbolFilter.Type).OfType<INamedTypeSymbol>())
            {
                var display = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", "", StringComparison.Ordinal);
                if (display.Equals(query, StringComparison.OrdinalIgnoreCase)
                    || t.Name.Equals(simple, StringComparison.OrdinalIgnoreCase)
                    || display.EndsWith("." + simple, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }

        return null;
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

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
