using DotNetMap.Core.Analysis;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class HierarchyQueriesTests
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
    public async Task FindImplementations_IOrderService_FindsOrderService()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-impl-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var result = await HierarchyQueries.FindImplementationsAsync(store, "IOrderService");
            Assert.Contains(result.Hits, h => h.FullName.Contains("OrderService", StringComparison.Ordinal)
                                              && !h.FullName.Contains("IOrder", StringComparison.Ordinal));
            Assert.Contains(result.Hits, h => h.File is not null && h.Line is > 0);

            var md = HierarchyQueries.FormatImplementations(result, "md");
            Assert.Contains("Implementations", md);
            Assert.Contains("OrderService", md);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FindOverrides_Adjust_FindsVipOverride()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-ov-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var result = await HierarchyQueries.FindOverridesAsync(store, "OrderCalculator.Adjust");
            Assert.Contains(result.Hits, h =>
                h.ContainingType.Contains("VipOrderCalculator", StringComparison.Ordinal)
                && h.DisplayName.Contains("Adjust", StringComparison.Ordinal));

            var md = HierarchyQueries.FormatOverrides(result, "md");
            Assert.Contains("Overrides", md);
            Assert.Contains("VipOrderCalculator", md);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static void Cleanup(string dbPath)
    {
        try { File.Delete(dbPath); } catch { /* ignore */ }
        try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
        try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
    }
}
