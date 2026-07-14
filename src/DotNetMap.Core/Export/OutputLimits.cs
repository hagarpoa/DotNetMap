namespace DotNetMap.Core.Export;

/// <summary>Hard caps for AI context (DNM-006).</summary>
public static class OutputLimits
{
    public const int DefaultMaxChars = 12_000;
    public const int DefaultMaxRelations = 20;
    public const int DefaultMaxMembers = 80;
    public const int DefaultMaxSearchHits = 50;
    public const int CompactRelationNameMax = 80;
}
