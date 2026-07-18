using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Store;

public sealed record TypeSummaryRow(
    string FullName,
    string Kind,
    string Accessibility,
    string? Summary,
    int TokenEstimate,
    int MemberCount);

public sealed record ProjectSummaryRow(
    string Name,
    string Path,
    string? TargetFramework,
    bool IsTest,
    int TypeCount,
    int FileCount);

public sealed record MemberSummaryRow(
    string Name,
    string Kind,
    string Signature,
    string? Summary,
    int TokenEstimate);

public sealed record SearchHit(
    string Category, // "type" | "member" | "body"
    string Id,
    string Name,
    string? Display,
    string? Summary,
    string? ParentType,
    double? Rank,
    /// <summary>Relative path for body hits (DNM-013).</summary>
    string? RelativePath = null,
    /// <summary>1-based line of first match for body hits.</summary>
    int? Line = null,
    /// <summary>Single-line snippet around the match (body hits).</summary>
    string? Snippet = null);

/// <summary>Normalized relation row from <c>edges</c> (DNM-014).</summary>
public sealed record EdgeRow(
    string FromId,
    string ToId,
    string Kind,
    string? File = null,
    int? Line = null);

/// <summary>One hop in a multi-hop edge walk.</summary>
public sealed record GraphHop(
    string NodeId,
    string ViaId,
    string Kind,
    int Depth,
    string? File = null,
    int? Line = null);

public sealed record TypeDetail(
    string Id,
    string FullName,
    string Kind,
    string Accessibility,
    string? Summary,
    int? StartLine,
    int? EndLine,
    int SizeChars,
    string? RelativePath,
    string DependenciesJson,
    string ConsumersJson,
    int TokenEstimate,
    IReadOnlyList<MemberDetail> Members,
    /// <summary>All partial declaration sites (DNM-016). Empty when single-file / unknown.</summary>
    IReadOnlyList<DeclarationLocation> Locations = null!,
    bool IsGenerated = false)
{
    public int LineCount =>
        StartLine is int s && EndLine is int e && e >= s ? e - s + 1 : 0;

    // Record optional with default null! → normalize for callers
    public IReadOnlyList<DeclarationLocation> AllLocations =>
        Locations is { Count: > 0 } ? Locations : [];
}

public sealed record MemberDetail(
    string Id,
    string Name,
    string Kind,
    string Signature,
    string Accessibility,
    string? ReturnType,
    string? Summary,
    int? StartLine,
    int? EndLine,
    int SizeChars,
    string DependenciesJson,
    string ConsumersJson,
    int TokenEstimate,
    string? RelativePath = null,
    string? ParentTypeFullName = null)
{
    public int LineCount =>
        StartLine is int s && EndLine is int e && e >= s ? e - s + 1 : 0;
}
