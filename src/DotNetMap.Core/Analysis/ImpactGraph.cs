using System.Text;
using System.Text.Json;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Store;

namespace DotNetMap.Core.Analysis;

/// <summary>
/// Compact multi-hop impact graph for AI (DNM-015).
/// Hop 0 can use live SymbolFinder; further hops prefer indexed edges only (fast).
/// </summary>
public static class ImpactGraph
{
    public enum Direction
    {
        Both,
        Inbound,
        Outbound
    }

    public sealed record Node(string Id, string Name, string Kind, int Depth);

    public sealed record Edge(
        string FromId,
        string ToId,
        string Kind,
        string? File = null,
        int? Line = null);

    public sealed record Result(
        string RootId,
        string RootName,
        string RootKind,
        int Depth,
        Direction Direction,
        bool LiveHop0,
        IReadOnlyList<Node> Nodes,
        IReadOnlyList<Edge> Edges,
        bool Truncated,
        IReadOnlyList<string> Notes);

    public static async Task<Result> BuildAsync(
        MapStore store,
        string symbolName,
        int depth = 2,
        int maxNodes = 40,
        Direction direction = Direction.Both,
        bool liveHop0 = true,
        CancellationToken cancellationToken = default)
    {
        depth = Math.Clamp(depth, 1, 4);
        maxNodes = Math.Clamp(maxNodes, 5, 80);

        var notes = new List<string>();
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var edges = new List<Edge>();
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string Id, string Name, string Kind, int Depth)>();

        // Resolve root: prefer member, then type
        MemberDetail? rootMember = store.GetMemberDetail(symbolName);
        TypeDetail? rootType = rootMember is null ? store.GetTypeDetail(symbolName) : null;

        string rootId;
        string rootName;
        string rootKind;

        if (rootMember is not null)
        {
            rootId = rootMember.Id;
            rootName = $"{rootMember.ParentTypeFullName}.{rootMember.Name}";
            rootKind = rootMember.Kind;
        }
        else if (rootType is not null)
        {
            rootId = rootType.Id;
            rootName = rootType.FullName;
            rootKind = "type";
        }
        else
        {
            throw new InvalidOperationException(
                $"Symbol not found in index: {symbolName}. Try search first.");
        }

        void AddNode(string id, string name, string kind, int d)
        {
            if (nodes.ContainsKey(id) || nodes.Count >= maxNodes)
                return;
            nodes[id] = new Node(id, name, kind, d);
        }

        void AddEdge(string from, string to, string kind, string? file = null, int? line = null)
        {
            var key = $"{from}|{to}|{kind}|{file}|{line}";
            if (!edgeKeys.Add(key))
                return;
            edges.Add(new Edge(from, to, kind, file, line));
        }

        AddNode(rootId, rootName, rootKind, 0);
        queue.Enqueue((rootId, rootName, rootKind, 0));

        var truncated = false;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (id, name, kind, d) = queue.Dequeue();
            if (d >= depth)
                continue;

            var nextDepth = d + 1;
            var useLive = liveHop0 && d == 0;

            // --- Outbound ---
            if (direction is Direction.Both or Direction.Outbound)
            {
                if (kind is "method" or "constructor" or "property" or "field" or "event")
                {
                    var member = store.GetMemberDetail(id) ?? store.GetMemberDetail(name);
                    if (member is not null)
                    {
                        foreach (var r in RelationPresentation.Parse(member.DependenciesJson))
                        {
                            if (r.Kind != RelationKind.Calls && r.Kind != RelationKind.UsesInSignature
                                && r.Kind != RelationKind.UsesInMember)
                                continue;

                            var toKind = r.TargetId.StartsWith("method:", StringComparison.Ordinal) ? "method"
                                : r.TargetId.StartsWith("property:", StringComparison.Ordinal) ? "property"
                                : r.TargetId.StartsWith("field:", StringComparison.Ordinal) ? "field"
                                : "type";
                            var toName = RelationPresentation.ShortName(r);
                            if (nodes.Count >= maxNodes && !nodes.ContainsKey(r.TargetId))
                            {
                                truncated = true;
                                continue;
                            }

                            AddNode(r.TargetId, toName, toKind, nextDepth);
                            AddEdge(id, r.TargetId, r.Kind.ToString(), r.File, r.Line);
                            if (nextDepth < depth && nodes.ContainsKey(r.TargetId))
                                queue.Enqueue((r.TargetId, toName, toKind, nextDepth));
                        }
                    }
                }
                else if (kind == "type")
                {
                    var type = store.GetTypeDetail(id) ?? store.GetTypeDetail(name);
                    if (type is not null)
                    {
                        foreach (var r in RelationPresentation.Parse(type.DependenciesJson))
                        {
                            if (r.Kind is not (RelationKind.Inherits or RelationKind.Implements
                                or RelationKind.UsesInSignature))
                                continue;

                            var toName = RelationPresentation.ShortName(r);
                            if (nodes.Count >= maxNodes && !nodes.ContainsKey(r.TargetId))
                            {
                                truncated = true;
                                continue;
                            }

                            AddNode(r.TargetId, toName, "type", nextDepth);
                            AddEdge(id, r.TargetId, r.Kind.ToString(), r.File, r.Line);
                            if (nextDepth < depth)
                                queue.Enqueue((r.TargetId, toName, "type", nextDepth));
                        }
                    }

                    if (useLive)
                    {
                        try
                        {
                            var impls = await HierarchyQueries.FindImplementationsAsync(
                                store, name, max: Math.Min(15, maxNodes), cancellationToken)
                                .ConfigureAwait(false);
                            foreach (var h in impls.Hits)
                            {
                                if (nodes.Count >= maxNodes && !nodes.ContainsKey(h.TypeId))
                                {
                                    truncated = true;
                                    break;
                                }

                                AddNode(h.TypeId, h.FullName, "type", nextDepth);
                                AddEdge(h.TypeId, id, h.Kind, h.File, h.Line); // implementor -> interface
                                if (nextDepth < depth)
                                    queue.Enqueue((h.TypeId, h.FullName, "type", nextDepth));
                            }
                        }
                        catch (Exception ex)
                        {
                            notes.Add($"implementations hop0: {ex.Message}");
                        }
                    }
                }
            }

            // --- Inbound ---
            if (direction is Direction.Both or Direction.Inbound)
            {
                if (kind is "method" or "constructor" or "property" or "field" or "event")
                {
                    // Indexed consumers first
                    var member = store.GetMemberDetail(id) ?? store.GetMemberDetail(name);
                    if (member is not null)
                    {
                        foreach (var r in RelationPresentation.Parse(member.ConsumersJson))
                        {
                            if (r.Kind != RelationKind.ReferencedBy)
                                continue;
                            var fromKind = r.TargetId.StartsWith("method:", StringComparison.Ordinal) ? "method"
                                : r.TargetId.StartsWith("property:", StringComparison.Ordinal) ? "property"
                                : r.TargetId.StartsWith("type:", StringComparison.Ordinal) ? "type"
                                : "member";
                            var fromName = RelationPresentation.ShortName(r);
                            if (nodes.Count >= maxNodes && !nodes.ContainsKey(r.TargetId))
                            {
                                truncated = true;
                                continue;
                            }

                            AddNode(r.TargetId, fromName, fromKind, nextDepth);
                            AddEdge(r.TargetId, id, "referencedBy", r.File, r.Line);
                            if (nextDepth < depth)
                                queue.Enqueue((r.TargetId, fromName, fromKind, nextDepth));
                        }
                    }

                    if (useLive)
                    {
                        try
                        {
                            var live = await ImpactAnalysis.GetCallersAsync(
                                store, name, updateDb: false, max: Math.Min(20, maxNodes), cancellationToken)
                                .ConfigureAwait(false);
                            foreach (var r in live.Callers)
                            {
                                var fromKind = r.TargetId.StartsWith("method:", StringComparison.Ordinal) ? "method"
                                    : r.TargetId.StartsWith("property:", StringComparison.Ordinal) ? "property"
                                    : "member";
                                var fromName = RelationPresentation.ShortName(r);
                                if (nodes.Count >= maxNodes && !nodes.ContainsKey(r.TargetId))
                                {
                                    truncated = true;
                                    break;
                                }

                                AddNode(r.TargetId, fromName, fromKind, nextDepth);
                                AddEdge(r.TargetId, id, "referencedBy", r.File, r.Line);
                                if (nextDepth < depth)
                                    queue.Enqueue((r.TargetId, fromName, fromKind, nextDepth));
                            }
                        }
                        catch (Exception ex)
                        {
                            notes.Add($"callers hop0: {ex.Message}");
                        }
                    }
                }
                else if (kind == "type")
                {
                    var type = store.GetTypeDetail(id) ?? store.GetTypeDetail(name);
                    if (type is not null)
                    {
                        foreach (var r in RelationPresentation.Parse(type.ConsumersJson))
                        {
                            var fromName = RelationPresentation.ShortName(r);
                            if (nodes.Count >= maxNodes && !nodes.ContainsKey(r.TargetId))
                            {
                                truncated = true;
                                continue;
                            }

                            AddNode(r.TargetId, fromName, "type", nextDepth);
                            AddEdge(r.TargetId, id, "referencedBy", r.File, r.Line);
                            if (nextDepth < depth)
                                queue.Enqueue((r.TargetId, fromName, "type", nextDepth));
                        }
                    }

                    if (useLive)
                    {
                        try
                        {
                            var live = await ImpactAnalysis.GetTypeConsumersAsync(
                                store, name, updateDb: false, max: Math.Min(20, maxNodes), cancellationToken)
                                .ConfigureAwait(false);
                            foreach (var r in live.Consumers)
                            {
                                var fromName = RelationPresentation.ShortName(r);
                                if (nodes.Count >= maxNodes && !nodes.ContainsKey(r.TargetId))
                                {
                                    truncated = true;
                                    break;
                                }

                                AddNode(r.TargetId, fromName, "type", nextDepth);
                                AddEdge(r.TargetId, id, "referencedBy", r.File, r.Line);
                                if (nextDepth < depth)
                                    queue.Enqueue((r.TargetId, fromName, "type", nextDepth));
                            }
                        }
                        catch (Exception ex)
                        {
                            notes.Add($"consumers hop0: {ex.Message}");
                        }
                    }
                }
            }
        }

        if (nodes.Count >= maxNodes)
            truncated = true;

        notes.Add(liveHop0
            ? "Hop 0 used live SymbolFinder where needed; deeper hops use index edges only."
            : "Index-only edges (no live SymbolFinder).");

        return new Result(
            rootId,
            rootName,
            rootKind,
            depth,
            direction,
            liveHop0,
            nodes.Values.OrderBy(n => n.Depth).ThenBy(n => n.Name).ToList(),
            edges,
            truncated,
            notes);
    }

    public static string Format(Result result, string format = "md")
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                root = new { id = result.RootId, name = result.RootName, kind = result.RootKind },
                depth = result.Depth,
                direction = result.Direction.ToString().ToLowerInvariant(),
                liveHop0 = result.LiveHop0,
                nodeCount = result.Nodes.Count,
                edgeCount = result.Edges.Count,
                truncated = result.Truncated,
                notes = result.Notes,
                nodes = result.Nodes,
                edges = result.Edges.Select(e => new
                {
                    from = e.FromId,
                    to = e.ToId,
                    kind = e.Kind,
                    file = e.File,
                    line = e.Line
                })
            }, JsonOpts);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Impact: {result.RootName}");
        sb.AppendLine();
        sb.AppendLine($"- Kind: `{result.RootKind}` | Depth: {result.Depth} | Direction: `{result.Direction.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Nodes: {result.Nodes.Count} | Edges: {result.Edges.Count}"
                      + (result.Truncated ? " | **truncated: true**" : ""));
        sb.AppendLine($"- Live hop0: {result.LiveHop0}");
        sb.AppendLine();

        sb.AppendLine("## Nodes by depth");
        sb.AppendLine();
        foreach (var group in result.Nodes.GroupBy(n => n.Depth).OrderBy(g => g.Key))
        {
            sb.AppendLine($"### Depth {group.Key}");
            foreach (var n in group)
                sb.AppendLine($"- `{n.Name}` ({n.Kind})");
            sb.AppendLine();
        }

        sb.AppendLine("## Edges");
        sb.AppendLine();
        var edgeShow = result.Edges.Take(OutputLimits.DefaultMaxRelations * 2).ToList();
        foreach (var e in edgeShow)
        {
            var from = result.Nodes.FirstOrDefault(n => n.Id == e.FromId)?.Name ?? e.FromId;
            var to = result.Nodes.FirstOrDefault(n => n.Id == e.ToId)?.Name ?? e.ToId;
            var site = e.File is null ? "" : e.Line is int l ? $" @ `{e.File}:L{l}`" : $" @ `{e.File}`";
            sb.AppendLine($"- `{from}` —[{e.Kind}]→ `{to}`{site}");
        }

        if (result.Edges.Count > edgeShow.Count)
            sb.AppendLine($"- … +{result.Edges.Count - edgeShow.Count} more edges");

        if (result.Notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Notes");
            foreach (var n in result.Notes)
                sb.AppendLine($"- {n}");
        }

        sb.AppendLine();
        sb.AppendLine($"_~{TokenEstimator.FromText(sb.ToString())} tokens_");
        var text = sb.ToString();
        if (text.Length <= OutputLimits.DefaultMaxChars)
            return text;
        return text[..(OutputLimits.DefaultMaxChars - 60)] + "\n\n_… truncated: true_\n";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
