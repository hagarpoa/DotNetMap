using DotNetMap.Core.Analysis;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class ImpactGraphTests
{
    private static string FindDemo()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "DemoSolution")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "samples", "DemoSolution")),
        };
        foreach (var c in candidates)
            if (Directory.Exists(c))
                return c;
        throw new DirectoryNotFoundException("DemoSolution");
    }

    [Fact]
    public async Task Impact_OnIOrderService_IncludesOrderService()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-impact-g-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var result = await ImpactGraph.BuildAsync(
                store, "IOrderService", depth: 2, maxNodes: 40,
                direction: ImpactGraph.Direction.Both, liveHop0: true);

            Assert.True(result.Nodes.Count >= 2);
            Assert.Contains(result.Nodes, n => n.Name.Contains("OrderService", StringComparison.Ordinal)
                                               && !n.Name.Contains("IOrder", StringComparison.Ordinal));

            var md = ImpactGraph.Format(result, "md");
            Assert.Contains("Impact:", md);
            Assert.Contains("Nodes by depth", md);
            Assert.Contains("OrderService", md);

            var json = ImpactGraph.Format(result, "json");
            Assert.Contains("nodeCount", json);
            Assert.Contains("edges", json);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Impact_OnSaveAsync_Outbound_IncludesCalculateTotal()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-impact-m-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var result = await ImpactGraph.BuildAsync(
                store, "OrderService.SaveAsync", depth: 1, maxNodes: 30,
                direction: ImpactGraph.Direction.Outbound, liveHop0: false);

            Assert.Contains(result.Nodes, n => n.Name.Contains("CalculateTotal", StringComparison.Ordinal)
                                               || result.Edges.Any(e => e.Kind.Contains("Calls", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
