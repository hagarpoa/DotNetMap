using NetMap.Core.Domain;
using NetMap.Core.Export;
using NetMap.Core.Store;

namespace NetMap.Tests;

public class SearchAndGetTests
{
    [Fact]
    public void Search_And_GetTypeDetail_Work()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"netmap-search-{Guid.NewGuid():N}.db");
        try
        {
            var projectId = Ids.Project("Demo");
            var nsId = Ids.Namespace(projectId, "Demo.Core");
            var typeId = Ids.Type("Demo.Core.OrderService");

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
                        Namespaces = [new NamespaceNode { Id = nsId, Name = "Demo.Core" }],
                        Types =
                        [
                            new TypeNode
                            {
                                Id = typeId,
                                NamespaceId = nsId,
                                Name = "OrderService",
                                FullName = "Demo.Core.OrderService",
                                Kind = TypeKind.Class,
                                Accessibility = "public",
                                Summary = "Default order pricing service.",
                                Dependencies =
                                [
                                    new RelationRef(RelationKind.Implements, Ids.Type("Demo.Core.IOrderService"), "Demo.Core.IOrderService")
                                ],
                                Members =
                                [
                                    new MemberNode
                                    {
                                        Id = Ids.Method("Demo.Core.OrderService.CalculateTotal"),
                                        Name = "CalculateTotal",
                                        Kind = MemberKind.Method,
                                        Signature = "decimal CalculateTotal(IReadOnlyList<OrderLine> lines)",
                                        Accessibility = "public",
                                        ReturnType = "decimal",
                                        Summary = "Calculates the total for order lines."
                                    },
                                    new MemberNode
                                    {
                                        Id = Ids.Method("Demo.Core.OrderService.SaveAsync"),
                                        Name = "SaveAsync",
                                        Kind = MemberKind.Method,
                                        Signature = "Task<Guid> SaveAsync(Order order)",
                                        Accessibility = "public",
                                        ReturnType = "Task<Guid>",
                                        Summary = "Persists the order."
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };

            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var typeHits = store.Search("OrderService", "type", 10);
            Assert.NotEmpty(typeHits);
            Assert.Contains(typeHits, h => h.Category == "type" && h.Display!.Contains("OrderService"));

            var memberHits = store.Search("CalculateTotal", "member", 10);
            Assert.NotEmpty(memberHits);
            Assert.Contains(memberHits, h => h.Name == "CalculateTotal");

            var allHits = store.Search("order", "all", 20);
            Assert.NotEmpty(allHits);

            var detail = store.GetTypeDetail("Demo.Core.OrderService");
            Assert.NotNull(detail);
            Assert.Equal(2, detail!.Members.Count);
            Assert.Contains("IOrderService", detail.DependenciesJson);

            var byShort = store.GetTypeDetail("OrderService");
            Assert.NotNull(byShort);

            var md = CompactExporter.TypeDetailToMarkdown(detail);
            Assert.Contains("CalculateTotal", md);
            Assert.Contains("OrderService", md);

            var searchMd = CompactExporter.SearchToMarkdown(typeHits, "OrderService");
            Assert.Contains("[type]", searchMd);

            var exportMembers = CompactExporter.ToMarkdown(store, new ExportOptions { IncludeMembers = true });
            Assert.Contains("CalculateTotal", exportMembers);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
