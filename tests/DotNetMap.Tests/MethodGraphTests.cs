using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class MethodGraphTests
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
    public async Task Index_Extracts_Calls_And_LineCounts()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true });
        var map = result.Map;

        var orderService = map.Projects.SelectMany(p => p.Types)
            .First(t => t.Name == "OrderService");
        var save = orderService.Members.First(m => m.Name == "SaveAsync");

        Assert.True(save.Span.StartLine is > 0);
        Assert.True(save.Span.EndLine is > 0);
        Assert.True(save.Span.LineCount >= 1);
        Assert.Contains(save.Dependencies, d =>
            d.Kind == RelationKind.Calls
            && d.TargetName.Contains("CalculateTotal", StringComparison.Ordinal));

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-mg-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);

            var typeDetail = store.GetTypeDetail("OrderService");
            Assert.NotNull(typeDetail);
            var m = typeDetail!.Members.First(x => x.Name == "SaveAsync");
            Assert.True(m.LineCount >= 1);
            Assert.Contains("calls", m.DependenciesJson, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrEmpty(m.RelativePath));

            var member = store.GetMemberDetail("OrderService.SaveAsync");
            Assert.NotNull(member);
            Assert.Equal("SaveAsync", member!.Name);

            var md = CompactExporter.MemberDetailToMarkdown(member);
            Assert.Contains("lines", md, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("L", md);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Callers_Finds_SaveAsync_Caller_Of_CalculateTotal()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true });
        using var loader = WorkspaceLoader.Create();
        var solution = await loader.OpenAsync(demo);
        var calc = await MethodCallersExtractor.FindMethodSymbolAsync(
            solution, "CalculateTotal", "Demo.App.OrderService");
        Assert.NotNull(calc);

        var callers = await new MethodCallersExtractor().FindCallersAsync(solution, calc!);
        Assert.Contains(callers, c => c.TargetName.Contains("SaveAsync", StringComparison.Ordinal));
    }
}
