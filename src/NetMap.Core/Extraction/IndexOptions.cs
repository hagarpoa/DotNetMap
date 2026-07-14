using NetMap.Core.Domain;

namespace NetMap.Core.Extraction;

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
    /// When true, reuse unchanged projects from an existing index (project-level invalidation).
    /// Falls back to full index if DB missing, solution path/flags differ, or store cannot load.
    /// </summary>
    public bool ChangedOnly { get; init; }

    /// <summary>Existing map for --changed-only (loaded by CLI/store).</summary>
    public SolutionMap? PreviousMap { get; init; }

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

    public string NetMapVersion { get; init; } = "0.1.0";

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
