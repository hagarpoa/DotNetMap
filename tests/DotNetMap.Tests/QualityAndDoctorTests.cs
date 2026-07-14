using DotNetMap.Core.Analysis;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class QualityAndDoctorTests
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
    public async Task Quality_And_Doctor_OnIndexedSample()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-doc-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = MapStore.Open(dbPath))
            {
                store.WriteMap(map);
                var q = IndexQuality.Compute(store);
                Assert.True(q.TypeCount >= 4);
                Assert.True(q.MethodsCount > 0);
                Assert.True(q.MethodsWithCalls > 0);
                Assert.False(string.IsNullOrEmpty(q.Grade));

                var md = IndexQuality.FormatMarkdown(q);
                Assert.Contains("Quality", md);
                Assert.Contains("Grade", md);
            }

            var doctor = Doctor.Run(dbPath);
            Assert.Contains(doctor.Checks, c => c.Name == "database" && c.Pass);
            Assert.Contains(doctor.Checks, c => c.Name == "index_data" && c.Pass);
            Assert.Contains(doctor.Checks, c => c.Name == "msbuild");
            var dmd = Doctor.FormatMarkdown(doctor);
            Assert.Contains("doctor", dmd, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
