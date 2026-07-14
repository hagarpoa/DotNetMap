using NetMap.Core.Domain;

namespace NetMap.Core.Extraction;

/// <summary>
/// Orchestrates workspace load + structure extraction + optional consumers + incremental reuse.
/// </summary>
public sealed class SolutionIndexer
{
    public async Task<IndexResult> IndexAsync(
        string path,
        IndexOptions options,
        CancellationToken cancellationToken = default)
    {
        options.Progress?.Report("Registering MSBuild...");
        using var loader = WorkspaceLoader.Create();

        var entry = WorkspaceLoader.ResolveEntryPath(path);
        options.Progress?.Report($"Opening {entry}...");

        var solution = await loader.OpenAsync(entry, cancellationToken).ConfigureAwait(false);

        if (loader.Diagnostics.Count > 0)
        {
            foreach (var d in loader.Diagnostics.Take(20))
                options.Progress?.Report($"workspace: {d}");
            if (loader.Diagnostics.Count > 20)
                options.Progress?.Report($"workspace: ... +{loader.Diagnostics.Count - 20} more");
        }

        var canIncremental = options.ChangedOnly
                             && options.PreviousMap is not null
                             && PathsEqual(options.PreviousMap.Path, entry)
                             && options.PreviousMap.IncludePrivate == options.IncludePrivate
                             && options.PreviousMap.IncludeTest == options.IncludeTest;

        string? note = null;
        if (options.ChangedOnly && !canIncremental)
        {
            note = "changed-only requested but full reindex required (missing index, path/flags mismatch).";
            options.Progress?.Report($"warning: {note}");
            options = new IndexOptions
            {
                IncludePrivate = options.IncludePrivate,
                IncludeTest = options.IncludeTest,
                FullRelations = options.FullRelations,
                RelationScopes = options.RelationScopes,
                LightDeps = options.LightDeps,
                ChangedOnly = false,
                PreviousMap = null,
                NetMapVersion = options.NetMapVersion,
                Progress = options.Progress
            };
        }
        else if (canIncremental)
        {
            options.Progress?.Report("incremental: project-level change detection enabled");
        }

        var extractor = new StructureExtractor(options);
        var (map, reused, reindexed, skippedTest) =
            await extractor.ExtractAsync(solution, entry, cancellationToken).ConfigureAwait(false);

        var scopes = options.EffectiveRelationScopes();
        if (scopes.Count > 0)
        {
            if (scopes.Any(s => s.Kind == RelationScopeKind.Full))
                options.Progress?.Report("warning: full consumer graph can be slow on large solutions.");

            // Consumers need fresh symbols; re-run on types from reindexed projects only when incremental
            var consumers = new ConsumersExtractor(options.Progress);
            if (canIncremental && reused > 0 && !scopes.Any(s => s.Kind == RelationScopeKind.Full))
            {
                // Still apply scopes against full map types (previous consumers may be stale for dependents)
                await consumers.ApplyAsync(map, solution, scopes, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await consumers.ApplyAsync(map, solution, scopes, cancellationToken).ConfigureAwait(false);
            }

            map = CloneWithMode(map, IndexMode.FullRelations);
        }

        options.Progress?.Report(
            $"done: {map.Projects.Count} projects ({reused} reused, {reindexed} reindexed), " +
            $"{map.Projects.Sum(p => p.Types.Count)} types, {map.Projects.Sum(p => p.Types.Sum(t => t.Members.Count))} members");

        return new IndexResult
        {
            Map = map,
            WasIncremental = canIncremental && reused > 0,
            ProjectsReused = reused,
            ProjectsReindexed = reindexed,
            ProjectsSkippedTest = skippedTest,
            IncrementalNote = note
        };
    }

    /// <summary>
    /// Open an already-indexed solution path and fill consumers for scopes, mutating the in-memory map.
    /// </summary>
    public async Task ApplyConsumersAsync(
        SolutionMap map,
        IReadOnlyList<RelationScope> scopes,
        CancellationToken cancellationToken = default)
    {
        if (scopes.Count == 0)
            return;

        using var loader = WorkspaceLoader.Create();
        var entry = WorkspaceLoader.ResolveEntryPath(map.Path);
        var solution = await loader.OpenAsync(entry, cancellationToken).ConfigureAwait(false);
        var consumers = new ConsumersExtractor(null);
        await consumers.ApplyAsync(map, solution, scopes, cancellationToken).ConfigureAwait(false);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private static SolutionMap CloneWithMode(SolutionMap map, IndexMode mode) => new()
    {
        Id = map.Id,
        Name = map.Name,
        Path = map.Path,
        FileHash = map.FileHash,
        Mode = mode,
        IncludePrivate = map.IncludePrivate,
        IncludeTest = map.IncludeTest,
        IndexedAtUtc = map.IndexedAtUtc,
        NetMapVersion = map.NetMapVersion,
        Projects = map.Projects
    };
}
