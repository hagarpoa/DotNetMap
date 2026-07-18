using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

/// <summary>DNM-017 — generated source policy.</summary>
public class GeneratedCodeTests
{
    private static string FindDemoSolution()
    {
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

        throw new DirectoryNotFoundException("Could not locate samples/DemoSolution.");
    }

    [Fact]
    public void IsGeneratedSourcePath_DetectsCommonPatterns()
    {
        Assert.True(Visibility.IsGeneratedSourcePath(@"C:\src\Foo.g.cs"));
        Assert.True(Visibility.IsGeneratedSourcePath("OrderGenerated.g.cs"));
        Assert.True(Visibility.IsGeneratedSourcePath("Form1.Designer.cs"));
        Assert.True(Visibility.IsGeneratedSourcePath("path/Generated/X.cs"));
        Assert.False(Visibility.IsGeneratedSourcePath("OrderService.cs"));
        Assert.True(Visibility.ShouldSkipDocument("Foo.g.cs", includeGenerated: false));
        Assert.False(Visibility.ShouldSkipDocument("Foo.g.cs", includeGenerated: true));
        Assert.True(Visibility.ShouldSkipDocument(@"C:\proj\obj\Debug\x.cs", includeGenerated: true));
    }

    [Fact]
    public async Task DefaultIndex_ExcludesGenerated_File()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            IncludeGenerated = false
        });

        Assert.False(result.Map.IncludeGenerated);
        Assert.DoesNotContain(
            result.Map.Projects.SelectMany(p => p.Types),
            t => t.FullName.Contains("OrderGenerated", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Map.Projects.SelectMany(p => p.Files),
            f => f.RelativePath.Contains("OrderGenerated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IncludeGenerated_IndexesAndLabels()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            IncludeGenerated = true
        });

        Assert.True(result.Map.IncludeGenerated);
        var genType = result.Map.Projects.SelectMany(p => p.Types)
            .FirstOrDefault(t => t.FullName.Contains("OrderGenerated", StringComparison.Ordinal));
        Assert.NotNull(genType);
        Assert.True(genType!.IsGenerated);

        var genFile = result.Map.Projects.SelectMany(p => p.Files)
            .FirstOrDefault(f => f.RelativePath.Contains("OrderGenerated", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(genFile);
        Assert.True(genFile!.IsGenerated);

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-gen-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(result.Map);
            Assert.True(store.GetStatus().IncludeGenerated);

            var detail = store.GetTypeDetail("OrderGenerated");
            Assert.NotNull(detail);
            Assert.True(detail!.IsGenerated);

            var md = CompactExporter.TypeDetailToMarkdown(detail);
            Assert.Contains("generated", md, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
