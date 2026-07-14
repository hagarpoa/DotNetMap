using DotNetMap.Core.Analysis;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class IndexStalenessTests
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
    public async Task FreshIndex_IsNotStale()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-stale-ok-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);
            var report = IndexStaleness.Check(store);
            Assert.False(report.IsStale, string.Join(" | ", report.Details));
            Assert.NotEmpty(report.OkProjects);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ChangedFile_IsStale()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-stale-ch-{Guid.NewGuid():N}.db");

        // Work on a copy of a source file path from the map
        var target = map.Projects.SelectMany(p => p.Files).First(f => f.RelativePath.EndsWith("OrderService.cs", StringComparison.OrdinalIgnoreCase));
        var original = File.ReadAllText(target.AbsolutePath);

        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            File.WriteAllText(target.AbsolutePath, original + "\n// touch for staleness\n");
            var report = IndexStaleness.Check(store);
            Assert.True(report.IsStale);
            Assert.NotEmpty(report.StaleProjects);
            Assert.Contains(report.Details, d => d.Contains("changed", StringComparison.OrdinalIgnoreCase)
                                                 || d.Contains("OrderService", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { File.WriteAllText(target.AbsolutePath, original); } catch { /* ignore */ }
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
