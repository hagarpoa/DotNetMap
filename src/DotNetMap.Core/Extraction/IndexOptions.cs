using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Extraction;

public sealed class IndexOptions
{
    /// <summary>Include private/protected members. Default: public + internal only.</summary>
    public bool IncludePrivate { get; init; }

    /// <summary>Include test projects. Default: exclude.</summary>
    public bool IncludeTest { get; init; }

    /// <summary>
    /// Legacy flag: equivalent to RelationScopes = [full].
    /// Prefer explicit --relations type:X / project:Y / full.
    /// </summary>
    public bool FullRelations { get; init; }

    /// <summary>
    /// Scoped consumer discovery. Empty = skip SymbolFinder (default, fast).
    /// </summary>
    public IReadOnlyList<RelationScope> RelationScopes { get; init; } = [];

    /// <summary>Include light deps: inherits, implements, signature/member type uses.</summary>
    public bool LightDeps { get; init; } = true;

    /// <summary>
    /// When false (default), method <c>calls</c> only store solution-local targets (DNM-007).
    /// BCL / NuGet calls are omitted from the index.
    /// </summary>
    public bool IncludeExternalCalls { get; init; }

    /// <summary>
    /// When false (default), member signature type deps omit BCL/NuGet types (Task, etc.).
    /// Structural type deps (inherits/implements) are always kept.
    /// </summary>
    public bool IncludeExternalSignatureDeps { get; init; }

    /// <summary>
    /// When true, reuse unchanged projects from an existing index (project-level invalidation).
    /// Falls back to full index if DB missing, solution path/flags differ, or store cannot load.
    /// </summary>
    public bool ChangedOnly { get; init; }

    /// <summary>Existing map for --changed-only (loaded by CLI/store).</summary>
    public SolutionMap? PreviousMap { get; init; }

    /// <summary>
    /// Project name patterns to skip (substring or simple <c>*</c> glob). From config <c>excludeProjects</c>.
    /// </summary>
    public IReadOnlyList<string> ExcludeProjectPatterns { get; init; } = [];

    /// <summary>Max outbound calls stored per method body (default 30).</summary>
    public int MaxCallsPerMethod { get; init; } = 30;

    /// <summary>
    /// When true, index full source file text into body_fts for <c>query --body</c> (DNM-013).
    /// Heavier; default off.
    /// </summary>
    public bool IndexBody { get; init; }

    /// <summary>
    /// When true, include source-generator / designer / <c>*.g.cs</c> files and generated symbols (DNM-017).
    /// Default off. Indexed nodes are labelled <c>isGenerated</c>.
    /// </summary>
    public bool IncludeGenerated { get; init; }

    /// <summary>Assembly names of projects in the solution (for external filtering).</summary>
    public IReadOnlySet<string> SolutionAssemblyNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IndexMode Mode
    {
        get
        {
            var scopes = EffectiveRelationScopes();
            if (scopes.Count > 0)
                return IndexMode.FullRelations;
            return LightDeps ? IndexMode.StructureLightDeps : IndexMode.Structure;
        }
    }

    public string DotNetMapVersion { get; init; } = "1.0.0";

    public IProgress<string>? Progress { get; init; }

    public IReadOnlyList<RelationScope> EffectiveRelationScopes()
    {
        if (RelationScopes.Count > 0)
            return RelationScopes;
        if (FullRelations)
            return [new RelationScope(RelationScopeKind.Full, null)];
        return [];
    }
}
