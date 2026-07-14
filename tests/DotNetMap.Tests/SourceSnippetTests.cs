using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Source;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class SourceSnippetTests
{
    [Fact]
    public void TryRead_ReturnsNumberedLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snip-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(path, "line1\nline2\nline3\nline4\n");
            var snip = SourceSnippetReader.TryRead(
                Path.GetFileName(path),
                2,
                3,
                new SourceSnippetOptions
                {
                    AbsolutePathHint = path,
                    SolutionPath = Path.GetDirectoryName(path),
                    ContextLines = 0,
                    MaxChars = 4000
                });

            Assert.NotNull(snip);
            Assert.Contains("line2", snip!.Text);
            Assert.Contains("line3", snip.Text);
            Assert.DoesNotContain("line1", snip.Text);
            Assert.Equal(2, snip.StartLine);
            Assert.Equal(3, snip.EndLine);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryRead_RejectsPathOutsideSolutionRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(Path.GetTempPath(), $"out-{Guid.NewGuid():N}.cs");
        File.WriteAllText(outside, "secret\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                SourceSnippetReader.TryRead(
                    "out.cs",
                    1,
                    1,
                    new SourceSnippetOptions
                    {
                        AbsolutePathHint = outside,
                        SolutionPath = root
                    }));
        }
        finally
        {
            try { File.Delete(outside); } catch { /* ignore */ }
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Get_WithSnippet_IncludesSourceBody()
    {
        var demo = FindDemo();
        var map = (await new SolutionIndexer().IndexAsync(demo, new IndexOptions { LightDeps = true })).Map;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-snip-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(map);
            var member = store.GetMemberDetail("SaveAsync");
            Assert.NotNull(member);

            var md = CompactExporter.MemberDetailToMarkdown(member!, new ExportOptions
            {
                Detail = DetailLevel.Compact,
                IncludeSnippet = true,
                SolutionPath = store.GetStatus().SolutionPath,
                ResolveAbsolutePath = store.ResolveFileAbsolutePath
            });

            Assert.Contains("## Source", md);
            Assert.Contains("CalculateTotal", md); // body of SaveAsync
            Assert.Contains("```csharp", md);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

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
}
