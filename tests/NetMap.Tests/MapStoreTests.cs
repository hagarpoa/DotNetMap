using NetMap.Core.Domain;
using NetMap.Core.Store;

namespace NetMap.Tests;

public class MapStoreTests
{
    [Fact]
    public void WriteMap_ThenGetStatus_ReturnsCounts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"netmap-test-{Guid.NewGuid():N}.db");
        try
        {
            var projectId = Ids.Project("Demo");
            var nsId = Ids.Namespace(projectId, "Demo");
            var typeId = Ids.Type("Demo.Order");

            var map = new SolutionMap
            {
                Id = Ids.Solution("/tmp/demo.sln"),
                Name = "Demo",
                Path = "/tmp/demo.sln",
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
                                Id = typeId,
                                NamespaceId = nsId,
                                Name = "Order",
                                FullName = "Demo.Order",
                                Kind = TypeKind.Class,
                                Accessibility = "public",
                                Summary = "An order aggregate.",
                                Dependencies =
                                [
                                    new RelationRef(RelationKind.Implements, Ids.Type("Demo.IOrder"), "Demo.IOrder")
                                ],
                                Members =
                                [
                                    new MemberNode
                                    {
                                        Id = Ids.Method("Demo.Order.Total()"),
                                        Name = "Total",
                                        Kind = MemberKind.Method,
                                        Signature = "decimal Total()",
                                        Accessibility = "public",
                                        ReturnType = "decimal",
                                        Summary = "Computes order total."
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };

            using (var store = MapStore.Open(dbPath))
            {
                store.WriteMap(map);
                var status = store.GetStatus();

                Assert.Equal(0, status.SchemaVersion);
                Assert.Equal("Demo", status.SolutionName);
                Assert.Equal(1, status.ProjectCount);
                Assert.Equal(1, status.TypeCount);
                Assert.Equal(1, status.MemberCount);
                Assert.True(status.TokenEstimateOverview > 0);
                Assert.Equal("structure+light-deps", status.IndexMode);
            }

            // Reopen
            using (var store = MapStore.Open(dbPath))
            {
                var status = store.GetStatus();
                Assert.True(store.HasSolutionData());
                Assert.Equal(1, status.TypeCount);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TokenEstimator_FromText_IsPositive()
    {
        Assert.Equal(0, TokenEstimator.FromText(null));
        Assert.Equal(0, TokenEstimator.FromText(""));
        Assert.True(TokenEstimator.FromText("abcd") >= 1);
    }
}
