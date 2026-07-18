using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

/// <summary>DNM-013 — optional source body FTS via --index-body / query --body.</summary>
public class BodyFtsTests
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

        throw new DirectoryNotFoundException("Could not locate samples/DemoSolution relative to test host.");
    }

    [Fact]
    public async Task IndexBody_Then_QueryBody_FindsTodoWithFileLine()
    {
        var demo = FindDemoSolution();
        var indexer = new SolutionIndexer();
        var result = await indexer.IndexAsync(demo, new IndexOptions
        {
            IncludePrivate = false,
            IncludeTest = false,
            LightDeps = true,
            IndexBody = true
        });

        Assert.True(result.Map.IndexBody);
        var fileCount = result.Map.Projects.Sum(p => p.Files.Count);
        Assert.True(fileCount >= 1,
            $"Expected source files on map after index; projects={result.Map.Projects.Count}, files={fileCount}");

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-body-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(result.Map);

            var status = store.GetStatus();
            Assert.True(status.IndexBody);
            Assert.True(status.BodyFileCount >= 1,
                $"body_file_count={status.BodyFileCount}; map files={fileCount}");
            Assert.True(store.HasBodyIndex());

            var hits = store.Search("TODO", kind: "all", max: 20, body: true);
            Assert.NotEmpty(hits);
            Assert.All(hits, h => Assert.Equal("body", h.Category));

            var todo = hits.FirstOrDefault(h =>
                (h.Snippet?.Contains("TODO", StringComparison.OrdinalIgnoreCase) ?? false)
                || (h.RelativePath?.Contains("OrderService", StringComparison.OrdinalIgnoreCase) ?? false));
            Assert.NotNull(todo);
            Assert.False(string.IsNullOrEmpty(todo!.RelativePath));
            Assert.NotNull(todo.Line);
            Assert.True(todo.Line > 0);

            var md = CompactExporter.SearchToMarkdown(hits, "TODO");
            Assert.Contains("[body]", md);
            Assert.Contains(":L", md);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WithoutIndexBody_HasBodyIndex_IsFalse()
    {
        var demo = FindDemoSolution();
        var indexer = new SolutionIndexer();
        var result = await indexer.IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            IndexBody = false
        });

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-nobody-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(result.Map);
            Assert.False(store.GetStatus().IndexBody);
            Assert.False(store.HasBodyIndex());
            Assert.Empty(store.Search("TODO", body: true, max: 10));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FindFirstMatchLine_ReturnsLineAndSnippet()
    {
        var content = "line1\n// TODO: fix me\nline3\n";
        var (line, snippet) = FtsQuery.FindFirstMatchLine(content, ["TODO"]);
        Assert.Equal(2, line);
        Assert.Contains("TODO", snippet, StringComparison.OrdinalIgnoreCase);
    }
}
