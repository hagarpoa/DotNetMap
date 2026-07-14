namespace DotNetMap.Core.Store;

public sealed record TypeSummaryRow(
    string FullName,
    string Kind,
    string Accessibility,
    string? Summary,
    int TokenEstimate,
    int MemberCount);

public sealed record MemberSummaryRow(
    string Name,
    string Kind,
    string Signature,
    string? Summary,
    int TokenEstimate);

public sealed record SearchHit(
    string Category, // "type" | "member"
    string Id,
    string Name,
    string? Display,
    string? Summary,
    string? ParentType,
    double? Rank);

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
    IReadOnlyList<MemberDetail> Members)
{
    public int LineCount =>
        StartLine is int s && EndLine is int e && e >= s ? e - s + 1 : 0;
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
