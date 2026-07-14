using DotNetMap.Core.Analysis;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class ImpactAnalysisTests
{
    private static string FindDemo()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "DemoSolution")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "samples", "DemoSolution")),
        };
        foreach (var c in candidates)
        {
            if (Directory.Exists(c))
                return c;
        }
        throw new DirectoryNotFoundException("DemoSolution not found");
    }

    [Fact]
    public async Task GetCallers_And_GetConsumers_Work()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-impact-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var callers = await ImpactAnalysis.GetCallersAsync(store, "CalculateTotal", updateDb: true);
            Assert.NotEmpty(callers.Callers);
            Assert.Contains(callers.Callers, c => c.TargetName.Contains("SaveAsync", StringComparison.Ordinal));
            Assert.True(callers.UpdatedDb);
            // DNM-005: each site has file + line
            Assert.Contains(callers.Callers, c =>
                c.TargetName.Contains("SaveAsync", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(c.File)
                && c.Line is > 0);

            var md = ImpactAnalysis.FormatCallers(callers, "md");
            Assert.Contains("References to", md);
            Assert.Contains("SaveAsync", md);
            Assert.Contains(":L", md); // site label file:Lline
            Assert.Contains("Sites:", md);

            var consumers = await ImpactAnalysis.GetTypeConsumersAsync(store, "IOrderService", updateDb: true);
            Assert.NotEmpty(consumers.Consumers);
            Assert.Contains(consumers.Consumers, c => c.TargetName.Contains("OrderService", StringComparison.Ordinal));

            var cmd = ImpactAnalysis.FormatConsumers(consumers, "md");
            Assert.Contains("Consumers", cmd);
            Assert.Contains("OrderService", cmd);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
