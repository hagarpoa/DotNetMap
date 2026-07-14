using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Extraction;

public sealed class IndexResult
{
    public required SolutionMap Map { get; init; }
    public bool WasIncremental { get; init; }
    public int ProjectsReused { get; init; }
    public int ProjectsReindexed { get; init; }
    public int ProjectsSkippedTest { get; init; }
    public string? IncrementalNote { get; init; }
}
