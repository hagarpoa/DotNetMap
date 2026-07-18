namespace DotNetMap.Core.Export;

/// <summary>Hard caps for AI context (DNM-006 / DNM-030).</summary>
public static class OutputLimits
{
    public const int DefaultMaxChars = 12_000;
    /// <summary>Absolute MCP/CLI ceiling — never exceed even if caller asks higher.</summary>
    public const int HardMaxChars = 24_000;
    public const int DefaultMaxRelations = 20;
    public const int HardMaxRelations = 100;
    public const int DefaultMaxMembers = 80;
    public const int HardMaxMembers = 200;
    public const int DefaultMaxSearchHits = 50;
    public const int HardMaxSearchHits = 100;
    public const int CompactRelationNameMax = 80;
    public const int HardMaxSnippetChars = 8_000;
    public const int HardMaxImpactNodes = 80;
    public const int HardMaxCallers = 100;

    public static int ClampChars(int value, int @default = DefaultMaxChars) =>
        Math.Clamp(value <= 0 ? @default : value, 200, HardMaxChars);

    public static int ClampSearchHits(int value, int @default = 15) =>
        Math.Clamp(value <= 0 ? @default : value, 1, HardMaxSearchHits);

    public static int ClampMembers(int value, int @default = DefaultMaxMembers) =>
        Math.Clamp(value <= 0 ? @default : value, 1, HardMaxMembers);

    public static int ClampSnippetChars(int value, int @default = 4_000) =>
        Math.Clamp(value <= 0 ? @default : value, 200, HardMaxSnippetChars);
}
