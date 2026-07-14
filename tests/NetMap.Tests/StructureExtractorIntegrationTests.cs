using NetMap.Core.Extraction;
using NetMap.Core.Store;

namespace NetMap.Tests;

public class StructureExtractorIntegrationTests
{
    private static string FindDemoSolution()
    {
        // tests run from bin/Debug/net10.0
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

        throw new DirectoryNotFoundException("Could not locate samples/DemoSolution relative to test host.");
    }

    [Fact]
    public async Task Index_DemoSolution_FindsOrderTypes()
    {
        var demo = FindDemoSolution();
        var indexer = new SolutionIndexer();
        var result = await indexer.IndexAsync(demo, new IndexOptions
        {
            IncludePrivate = false,
            IncludeTest = false,
            LightDeps = true,
            Progress = null
        });
        var map = result.Map;

        Assert.True(map.Projects.Count >= 2, $"Expected >= 2 projects, got {map.Projects.Count}");

        var allTypes = map.Projects.SelectMany(p => p.Types).ToList();
        var names = allTypes.Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(names, n => n.Contains("Order", StringComparison.Ordinal) && !n.Contains("OrderLine") && !n.Contains("OrderService") && !n.Contains("IOrder"));
        Assert.Contains(names, n => n.Contains("IOrderService", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("OrderService", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("OrderLine", StringComparison.Ordinal));

        var orderService = allTypes.First(t => t.FullName.Contains("OrderService", StringComparison.Ordinal)
                                               && !t.FullName.Contains("IOrder"));
        Assert.Contains(orderService.Dependencies, d =>
            d.Kind == Core.Domain.RelationKind.Implements
            && d.TargetName.Contains("IOrderService", StringComparison.Ordinal));

        Assert.True(orderService.Members.Count >= 2, "OrderService should have CalculateTotal and SaveAsync");

        // Persist round-trip
        var dbPath = Path.Combine(Path.GetTempPath(), $"netmap-demo-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);
            var status = store.GetStatus();
            Assert.True(status.TypeCount >= 4);
            Assert.True(status.MemberCount >= 4);
            Assert.True(status.FileCount >= 3);

            var listed = store.ListTypes();
            Assert.Contains(listed, t => t.FullName.Contains("OrderService", StringComparison.Ordinal));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
