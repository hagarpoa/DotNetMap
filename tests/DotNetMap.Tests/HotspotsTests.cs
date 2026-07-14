using DotNetMap.Core.Analysis;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class HotspotsTests
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
    public async Task Hotspots_BySize_And_Calls_ReturnResults()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-hot-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var bySize = Hotspots.Compute(store, Hotspots.Metric.Size, limit: 10);
            Assert.NotEmpty(bySize.Members);
            Assert.All(bySize.Members, m => Assert.True(m.Score > 0));

            var byCalls = Hotspots.Compute(store, Hotspots.Metric.Calls, limit: 10);
            Assert.Contains(byCalls.Members, m => m.Name.Contains("SaveAsync", StringComparison.Ordinal));

            var byTypes = Hotspots.Compute(store, Hotspots.Metric.Types, limit: 5);
            Assert.NotEmpty(byTypes.Types);

            var md = Hotspots.Format(bySize, "md");
            Assert.Contains("Hotspots", md);
            Assert.Contains("lines", md, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
