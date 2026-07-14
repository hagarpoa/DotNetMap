using NetMap.Core.Extraction;
using NetMap.Core.Store;

namespace NetMap.Tests;

public class IncrementalIndexTests
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
    public async Task ChangedOnly_SecondPass_ReusesProjects()
    {
        var demo = FindDemoSolution();
        var indexer = new SolutionIndexer();

        var first = await indexer.IndexAsync(demo, new IndexOptions { LightDeps = true });
        Assert.True(first.ProjectsReindexed >= 2);
        Assert.Equal(0, first.ProjectsReused);

        var second = await indexer.IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            ChangedOnly = true,
            PreviousMap = first.Map
        });

        Assert.True(second.ProjectsReused >= 2, $"Expected reused projects, got reused={second.ProjectsReused}, reindexed={second.ProjectsReindexed}");
        Assert.Equal(0, second.ProjectsReindexed);
        Assert.True(second.WasIncremental);
        Assert.Equal(first.Map.Projects.Sum(p => p.Types.Count), second.Map.Projects.Sum(p => p.Types.Count));
    }

    [Fact]
    public async Task LoadFullMap_RoundTrip_PreservesFileHashes()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true });
        var dbPath = Path.Combine(Path.GetTempPath(), $"netmap-inc-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = MapStore.Open(dbPath))
            {
                store.WriteMap(result.Map);
                var loaded = store.LoadFullMap();
                Assert.NotNull(loaded);
                Assert.Equal(result.Map.Projects.Count, loaded!.Projects.Count);
                foreach (var p in result.Map.Projects)
                {
                    var lp = loaded.Projects.First(x => x.Id == p.Id);
                    Assert.Equal(p.Files.Count, lp.Files.Count);
                    Assert.Equal(p.Types.Count, lp.Types.Count);
                    Assert.Equal(ProjectFingerprint.ComputeFromNode(p), ProjectFingerprint.ComputeFromNode(lp));
                }
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
    public void ProjectFingerprint_ChangesWhenContentDiffers()
    {
        var a = ContentHasher.Sha256Hex("a");
        var b = ContentHasher.Sha256Hex("b");
        Assert.NotEqual(a, b);
    }
}
