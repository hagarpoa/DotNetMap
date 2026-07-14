using DotNetMap.Core.Analysis;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class MemberReferencesTests
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
    public async Task Callers_OnProperty_LineTotal_FindsSites()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-pref-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var member = store.GetMemberDetail("OrderLine.LineTotal");
            Assert.NotNull(member);
            Assert.Equal("property", member!.Kind, ignoreCase: true);

            var result = await ImpactAnalysis.GetCallersAsync(store, "OrderLine.LineTotal");
            Assert.NotEmpty(result.Callers);
            Assert.Contains(result.Callers, c =>
                c.TargetName.Contains("GetSubtotal", StringComparison.Ordinal)
                || c.TargetName.Contains("CalculateTotal", StringComparison.Ordinal)
                || c.TargetName.Contains("LineTotal", StringComparison.Ordinal));
            Assert.Contains(result.Callers, c => c.File is not null && c.Line is > 0);

            var md = ImpactAnalysis.FormatCallers(result, "md");
            Assert.Contains(":L", md);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
