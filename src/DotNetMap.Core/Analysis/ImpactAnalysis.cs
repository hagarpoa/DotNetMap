using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Core.Analysis;

/// <summary>
/// On-demand impact queries (callers / type consumers) shared by CLI and MCP.
/// </summary>
public static class ImpactAnalysis
{
    public sealed record CallersResult(
        MemberDetail Member,
        string Label,
        IReadOnlyList<RelationRef> Callers,
        bool UpdatedDb);

    public sealed record ConsumersResult(
        TypeDetail Type,
        IReadOnlyList<RelationRef> Consumers,
        bool UpdatedDb);

    public static async Task<CallersResult> GetCallersAsync(
        MapStore store,
        string memberNameOrId,
        bool updateDb = false,
        int max = MemberReferencesExtractor.MaxSites,
        CancellationToken cancellationToken = default)
    {
        var member = store.GetMemberDetail(memberNameOrId)
                     ?? throw new InvalidOperationException(
                         $"Member not found in index: {memberNameOrId}. Index first or use Type.Member (method/property/field).");

        var status = store.GetStatus();
        if (string.IsNullOrEmpty(status.SolutionPath))
            throw new InvalidOperationException("Solution path missing from index. Re-run index.");

        using var loader = WorkspaceLoader.Create();
        var solution = await loader.OpenAsync(status.SolutionPath, cancellationToken).ConfigureAwait(false);

        var symbol = await MemberReferencesExtractor.FindMemberSymbolAsync(solution, member, cancellationToken)
            .ConfigureAwait(false);

        if (symbol is null)
            throw new InvalidOperationException(
                $"Could not resolve {member.Kind} symbol in workspace for '{member.ParentTypeFullName}.{member.Name}'.");

        var extractor = new MemberReferencesExtractor();
        var callers = await extractor.FindReferenceSitesAsync(solution, symbol, cancellationToken)
            .ConfigureAwait(false);
        if (max > 0 && callers.Count > max)
            callers = callers.Take(max).ToList();

        if (updateDb)
            store.SaveMemberConsumers(member.Id, callers);

        var label = $"{member.ParentTypeFullName}.{member.Name} ({member.Kind})";
        return new CallersResult(member, label, callers, updateDb);
    }

    public static async Task<ConsumersResult> GetTypeConsumersAsync(
        MapStore store,
        string typeNameOrId,
        bool updateDb = false,
        int max = ConsumersExtractor.MaxConsumersPerType,
        CancellationToken cancellationToken = default)
    {
        var type = store.GetTypeDetail(typeNameOrId, maxMembers: 1)
                   ?? throw new InvalidOperationException(
                       $"Type not found in index: {typeNameOrId}. Try search first.");

        var status = store.GetStatus();
        if (string.IsNullOrEmpty(status.SolutionPath))
            throw new InvalidOperationException("Solution path missing from index. Re-run index.");

        var skeleton = store.LoadMapSkeleton()
                       ?? throw new InvalidOperationException("Could not load map skeleton.");

        // Ensure the target type is in the skeleton with consumers list we can mutate
        var targetNode = skeleton.Projects.SelectMany(p => p.Types)
            .FirstOrDefault(t =>
                t.Id.Equals(type.Id, StringComparison.OrdinalIgnoreCase)
                || t.FullName.Equals(type.FullName, StringComparison.OrdinalIgnoreCase)
                || t.Name.Equals(typeNameOrId, StringComparison.OrdinalIgnoreCase));

        if (targetNode is null)
            throw new InvalidOperationException($"Type node '{type.FullName}' missing from map skeleton.");

        using var loader = WorkspaceLoader.Create();
        var solution = await loader.OpenAsync(status.SolutionPath, cancellationToken).ConfigureAwait(false);

        var scopes = new[] { RelationScope.Parse("type:" + type.FullName) };
        var extractor = new ConsumersExtractor();
        await extractor.ApplyAsync(skeleton, solution, scopes, cancellationToken).ConfigureAwait(false);

        // Re-resolve after ApplyAsync mutated consumers on matching nodes
        targetNode = skeleton.Projects.SelectMany(p => p.Types)
            .First(t => t.Id == type.Id || t.FullName == type.FullName);

        var consumers = (IReadOnlyList<RelationRef>)targetNode.Consumers;
        if (max > 0 && consumers.Count > max)
            consumers = consumers.Take(max).ToList();

        if (updateDb)
            store.SaveTypeConsumers([targetNode], IndexMode.FullRelations);

        // Refresh type detail for return (consumers_json may be updated)
        var refreshed = store.GetTypeDetail(type.FullName) ?? type;
        return new ConsumersResult(refreshed, consumers, updateDb);
    }

    public static string FormatCallers(
        CallersResult result,
        string format = "md",
        DetailLevel detail = DetailLevel.Compact)
    {
        var opts = new ExportOptions
        {
            Detail = detail,
            MaxRelations = OutputLimits.DefaultMaxRelations,
            MaxChars = OutputLimits.DefaultMaxChars
        };

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                method = result.Member.Id,
                name = result.Label,
                siteCount = result.Callers.Count,
                uniqueCallers = result.Callers.Select(c => c.TargetId).Distinct(StringComparer.Ordinal).Count(),
                updatedDb = result.UpdatedDb,
                sites = result.Callers.Select(c => new
                {
                    callerId = c.TargetId,
                    caller = RelationPresentation.ShortName(c),
                    file = c.File,
                    line = c.Line,
                    site = c.DisplaySiteLabel
                }),
                lines = new { start = result.Member.StartLine, end = result.Member.EndLine, count = result.Member.LineCount },
                file = result.Member.RelativePath
            }, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }

        var md = CompactExporter.CallersToMarkdown(result.Label, result.Callers, opts);
        if (result.UpdatedDb)
            md = md.TrimEnd() + "\n\n_Persisted to members.consumers_json (with file/line sites)._\n";
        return md;
    }

    public static string FormatConsumers(
        ConsumersResult result,
        string format = "md",
        DetailLevel detail = DetailLevel.Compact)
    {
        var opts = new ExportOptions
        {
            Detail = detail,
            MaxRelations = OutputLimits.DefaultMaxRelations,
            MaxChars = OutputLimits.DefaultMaxChars
        };

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                type = result.Type.Id,
                name = result.Type.FullName,
                count = result.Consumers.Count,
                updatedDb = result.UpdatedDb,
                consumers = result.Consumers.Select(c => new { c.TargetId, name = RelationPresentation.ShortName(c) }),
                lines = new { start = result.Type.StartLine, end = result.Type.EndLine, count = result.Type.LineCount },
                file = result.Type.RelativePath
            }, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }

        return CompactExporter.ConsumersToMarkdown(result.Type.FullName, result.Consumers, opts, result.UpdatedDb);
    }
}
