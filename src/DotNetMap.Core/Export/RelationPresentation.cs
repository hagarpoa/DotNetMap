using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Export;

/// <summary>
/// Parse and shape relation JSON for compact AI output (separates calls from signature deps).
/// </summary>
public static class RelationPresentation
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static IReadOnlyList<RelationRef> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json is "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<RelationRef>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<RelationRef> CallsOnly(IEnumerable<RelationRef> relations) =>
        relations.Where(r => r.Kind == RelationKind.Calls).ToList();

    public static IReadOnlyList<RelationRef> SignatureDepsOnly(IEnumerable<RelationRef> relations) =>
        relations.Where(r => r.Kind is RelationKind.UsesInSignature or RelationKind.UsesInMember).ToList();

    public static IReadOnlyList<RelationRef> StructuralOnly(IEnumerable<RelationRef> relations) =>
        relations.Where(r => r.Kind is RelationKind.Inherits or RelationKind.Implements).ToList();

    public static IReadOnlyList<RelationRef> ConsumersOnly(IEnumerable<RelationRef> relations) =>
        relations.Where(r => r.Kind == RelationKind.ReferencedBy).ToList();

    /// <summary>Short display name for tokens: Type.Method or last segment.</summary>
    public static string ShortName(RelationRef r)
    {
        var name = r.TargetName;
        if (string.IsNullOrEmpty(name))
            return r.TargetId;

        // Prefer Type.Method without full parameter list for compact tokens
        var paren = name.IndexOf('(');
        if (paren > 0)
            name = name[..paren];

        if (name.Length > OutputLimits.CompactRelationNameMax)
            name = name[..(OutputLimits.CompactRelationNameMax - 1)] + "…";

        return name;
    }

    public static RelationSlice Slice(
        string? json,
        DetailLevel detail,
        int maxRelations = OutputLimits.DefaultMaxRelations)
    {
        var all = Parse(json);
        var calls = CallsOnly(all);
        var signature = SignatureDepsOnly(all);
        var structural = StructuralOnly(all);
        var consumers = ConsumersOnly(all);

        var includeSignature = detail == DetailLevel.Full;
        var callList = Take(calls, maxRelations, out var callsTruncated);
        var sigList = includeSignature ? Take(signature, maxRelations, out var sigTrunc) : [];
        var sigTruncated = includeSignature && signature.Count > maxRelations;
        var structList = Take(structural, maxRelations, out var structTrunc);
        var consList = detail == DetailLevel.Full
            ? Take(consumers, maxRelations, out var consTrunc)
            : Take(consumers, Math.Min(5, maxRelations), out consTrunc);

        return new RelationSlice(
            callList.Select(ShortName).ToList(),
            callList,
            calls.Count,
            callsTruncated,
            sigList.Select(ShortName).ToList(),
            signature.Count,
            sigTruncated,
            structList.Select(ShortName).ToList(),
            structural.Count,
            structTrunc,
            consList.Select(ShortName).ToList(),
            consumers.Count,
            consTrunc,
            all.Count);
    }

    private static List<RelationRef> Take(IReadOnlyList<RelationRef> source, int max, out bool truncated)
    {
        if (source.Count <= max)
        {
            truncated = false;
            return source.ToList();
        }

        truncated = true;
        return source.Take(max).ToList();
    }
}

public sealed record RelationSlice(
    IReadOnlyList<string> CallNames,
    IReadOnlyList<RelationRef> Calls,
    int CallsTotal,
    bool CallsTruncated,
    IReadOnlyList<string> SignatureDepNames,
    int SignatureDepsTotal,
    bool SignatureDepsTruncated,
    IReadOnlyList<string> StructuralNames,
    int StructuralTotal,
    bool StructuralTruncated,
    IReadOnlyList<string> ConsumerNames,
    int ConsumersTotal,
    bool ConsumersTruncated,
    int AllTotal)
{
    public bool AnyTruncated =>
        CallsTruncated || SignatureDepsTruncated || StructuralTruncated || ConsumersTruncated;
}
