using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

/// <summary>DNM-025 language surface + DNM-018 multi-TFM + DNM-038 .sln/.slnx.</summary>
public class LanguageSurfaceAndTfmTests
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
            if (Directory.Exists(c))
                return c;
        }

        throw new DirectoryNotFoundException("samples/DemoSolution not found");
    }

    [Fact]
    public async Task Index_Slnx_FindsLanguageSurfaceAndMultiTfm()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true });
        var names = result.Map.Projects.SelectMany(p => p.Types).Select(t => t.FullName).ToList();

        Assert.Contains(names, n => n.Contains("Money", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Point2D", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("OrderStatus", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("IRepository", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("InMemoryRepository", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("NestedHost", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("TaggedAsyncService", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("SharedApi", StringComparison.Ordinal));

        var multi = result.Map.Projects.FirstOrDefault(p =>
            p.Name.Contains("MultiTfm", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(multi);
        Assert.False(string.IsNullOrEmpty(multi!.TargetFramework));
        Assert.Contains("net", multi.TargetFramework, StringComparison.OrdinalIgnoreCase);

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-surface-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(result.Map);
            var projects = store.ListProjects();
            Assert.Contains(projects, p =>
                p.Name.Contains("MultiTfm", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(p.TargetFramework));

            Assert.NotNull(store.GetTypeDetail("Money"));
            Assert.NotNull(store.GetTypeDetail("SharedApi"));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Index_ClassicSln_DoesNotCrash()
    {
        var demo = FindDemoSolution();
        var sln = Path.Combine(demo, "DemoSolution.sln");
        Assert.True(File.Exists(sln), "DemoSolution.sln missing");

        var result = await new SolutionIndexer().IndexAsync(sln, new IndexOptions { LightDeps = true });
        Assert.True(result.Map.Projects.Count >= 2);
        Assert.True(result.Map.Projects.SelectMany(p => p.Types).Count() >= 4);
    }
}
