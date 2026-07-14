using DotNetMap.Core.Domain;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class ConsumersIntegrationTests
{
    private static string FindDemoSolution()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "DemoSolution")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "samples", "DemoSolution")),
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
    public async Task Index_WithRelationsType_FindsOrderServiceAsConsumerOfIOrderService()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            RelationScopes = [RelationScope.Parse("type:IOrderService")]
        });
        var map = result.Map;

        var iface = map.Projects.SelectMany(p => p.Types)
            .FirstOrDefault(t => t.Name == "IOrderService");
        Assert.NotNull(iface);

        Assert.Contains(iface!.Consumers, c =>
            c.Kind == RelationKind.ReferencedBy
            && c.TargetName.Contains("OrderService", StringComparison.Ordinal));

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-cons-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);
            var detail = store.GetTypeDetail("IOrderService");
            Assert.NotNull(detail);
            Assert.Contains("OrderService", detail!.ConsumersJson, StringComparison.Ordinal);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
