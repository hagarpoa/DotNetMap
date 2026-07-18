using DotNetMap.Core.Domain;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

/// <summary>DNM-014 — normalized edges table + multi-hop SQL walk.</summary>
public class EdgesTests
{
    private static string FindDemoSolution()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "DemoSolution")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "samples", "DemoSolution")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "samples", "DemoSolution")),
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) &&
                (File.Exists(Path.Combine(c, "DemoSolution.slnx")) || File.Exists(Path.Combine(c, "DemoSolution.sln"))))
                return c;
        }

        throw new DirectoryNotFoundException("Could not locate samples/DemoSolution.");
    }

    [Fact]
    public async Task WriteMap_MaterializesEdges_AndTwoHopQueryWorks()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            IncludePrivate = false
        });

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-edges-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(result.Map);

            var status = store.GetStatus();
            Assert.Equal(MapStore.CurrentSchemaVersion, status.SchemaVersion);
            Assert.True(status.EdgeCount > 0, $"expected edges, got {status.EdgeCount}");
            Assert.True(store.HasEdges());

            // OrderService implements IOrderService → outbound implements edge
            var orderService = result.Map.Projects.SelectMany(p => p.Types)
                .First(t => t.FullName.Contains("OrderService", StringComparison.Ordinal)
                            && !t.FullName.Contains("IOrder"));
            var outbound = store.GetOutboundEdges(orderService.Id);
            Assert.Contains(outbound, e =>
                e.Kind.Equals("implements", StringComparison.OrdinalIgnoreCase)
                && e.ToId.Contains("IOrderService", StringComparison.OrdinalIgnoreCase));

            // SaveAsync → CalculateTotal call edge
            var save = orderService.Members.First(m => m.Name == "SaveAsync");
            var calls = store.GetOutboundEdges(save.Id);
            Assert.Contains(calls, e => e.Kind.Equals("calls", StringComparison.OrdinalIgnoreCase));

            // 2-hop SQL walk from SaveAsync should reach at least the call target
            var hops = store.QueryGraphHops(save.Id, depth: 2, outbound: true, max: 40);
            Assert.NotEmpty(hops);
            Assert.Contains(hops, h => h.Depth == 1);
            Assert.All(hops, h => Assert.True(h.Depth is >= 1 and <= 2));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Migrate_FromJsonOnly_Db_BuildsEdges()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-migrate-edges-{Guid.NewGuid():N}.db");
        try
        {
            // Write a minimal map (creates edges with current writer)
            var projectId = Ids.Project("Demo");
            var nsId = Ids.Namespace(projectId, "Demo");
            var typeA = Ids.Type("Demo.A");
            var typeB = Ids.Type("Demo.B");
            var map = new SolutionMap
            {
                Id = Ids.Solution("/tmp/m.sln"),
                Name = "M",
                Path = "/tmp/m.sln",
                Mode = IndexMode.StructureLightDeps,
                Projects =
                [
                    new ProjectNode
                    {
                        Id = projectId,
                        Name = "Demo",
                        Path = "/tmp/Demo.csproj",
                        Namespaces = [new NamespaceNode { Id = nsId, Name = "Demo" }],
                        Types =
                        [
                            new TypeNode
                            {
                                Id = typeA,
                                NamespaceId = nsId,
                                Name = "A",
                                FullName = "Demo.A",
                                Kind = TypeKind.Class,
                                Accessibility = "public",
                                Dependencies =
                                [
                                    new RelationRef(RelationKind.Implements, typeB, "Demo.B")
                                ]
                            },
                            new TypeNode
                            {
                                Id = typeB,
                                NamespaceId = nsId,
                                Name = "B",
                                FullName = "Demo.B",
                                Kind = TypeKind.Interface,
                                Accessibility = "public"
                            }
                        ]
                    }
                ]
            };

            using (var store = MapStore.Open(dbPath))
            {
                store.WriteMap(map);
                Assert.True(store.HasEdges());
                // Simulate legacy: wipe edges, set schema 0, keep JSON
                using (var cmd = store.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM edges; UPDATE meta SET value='0' WHERE key='schema_version';";
                    cmd.ExecuteNonQuery();
                }
            }

            // Re-open → EnsureSchema migrates JSON → edges
            using (var store = MapStore.Open(dbPath))
            {
                Assert.True(store.HasEdges());
                Assert.Equal(MapStore.CurrentSchemaVersion, store.GetStatus().SchemaVersion);
                var outA = store.GetOutboundEdges(typeA);
                Assert.Contains(outA, e => e.ToId == typeB && e.Kind == "implements");

                var hops = store.QueryGraphHops(typeA, depth: 2, outbound: true);
                Assert.Contains(hops, h => h.NodeId == typeB && h.Depth == 1);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
