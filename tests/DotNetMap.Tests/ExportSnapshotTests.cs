using System.Text.RegularExpressions;
using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

/// <summary>DNM-026 — regression snapshot of DemoSolution export (paths/timestamps normalized).</summary>
public class ExportSnapshotTests
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

    private static string FindGoldenPath()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Goldens", "demo-export.md")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Goldens", "demo-export.md")),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        // Source tree relative to test project
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Goldens", "demo-export.md"));
    }

    internal static string NormalizeExport(string md, string solutionRoot)
    {
        var root = Path.GetFullPath(solutionRoot).Replace('\\', '/').TrimEnd('/');
        var normalized = md.Replace('\\', '/');

        // Absolute solution path (with optional trailing segments)
        normalized = Regex.Replace(
            normalized,
            Regex.Escape(root) + @"(/[^\s`]*)?",
            m => "$SOLUTION" + (m.Groups[1].Success ? m.Groups[1].Value : ""),
            RegexOptions.IgnoreCase);

        // Windows drive-letter leftovers
        normalized = Regex.Replace(normalized, @"[A-Za-z]:/[^`\s]*DemoSolution", "$SOLUTION");

        // Indexed timestamp
        normalized = Regex.Replace(
            normalized,
            @"Indexed \(UTC\):\s*\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z",
            "Indexed (UTC): $TIMESTAMP");

        // Token estimate can drift slightly with formatting — pin for snapshot stability
        normalized = Regex.Replace(
            normalized,
            @"Token estimate \(overview\): ~\d+",
            "Token estimate (overview): ~TOKENS");

        // Sort-stable: collapse consecutive blank lines
        normalized = Regex.Replace(normalized, @"(\r?\n){3,}", "\n\n");
        return normalized.TrimEnd() + "\n";
    }

    [Fact]
    public async Task Export_DemoSolution_MatchesGolden()
    {
        var demo = FindDemoSolution();
        var indexer = new SolutionIndexer();
        var result = await indexer.IndexAsync(demo, new IndexOptions
        {
            IncludePrivate = false,
            IncludeTest = false,
            LightDeps = true
        });

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-golden-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(result.Map);

            var md = CompactExporter.ToMarkdown(store, new ExportOptions
            {
                IncludeMembers = true,
                MaxTypes = 200,
                MaxMembersPerType = 50,
                IncludeDeps = true,
                Detail = DetailLevel.Compact
            });

            var actual = NormalizeExport(md, demo);
            var goldenPath = FindGoldenPath();
            var goldenDir = Path.GetDirectoryName(goldenPath)!;
            Directory.CreateDirectory(goldenDir);

            if (!File.Exists(goldenPath) ||
                string.Equals(Environment.GetEnvironmentVariable("DOTNETMAP_UPDATE_GOLDEN"), "1", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(goldenPath, actual);
                // First run / update mode: write golden into test project source tree as well
                var sourceGolden = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "Goldens", "demo-export.md"));
                Directory.CreateDirectory(Path.GetDirectoryName(sourceGolden)!);
                await File.WriteAllTextAsync(sourceGolden, actual);
            }

            Assert.True(File.Exists(goldenPath), $"Missing golden file: {goldenPath}");
            var expected = await File.ReadAllTextAsync(goldenPath);
            // Re-normalize expected in case golden was written on another OS path style
            expected = expected.Replace("\r\n", "\n");
            actual = actual.Replace("\r\n", "\n");

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                var diffHint = Path.Combine(Path.GetTempPath(), "dotnetmap-export-actual.md");
                await File.WriteAllTextAsync(diffHint, actual);
                Assert.Fail(
                    $"Export snapshot mismatch. Actual written to {diffHint}. " +
                    "Set DOTNETMAP_UPDATE_GOLDEN=1 to refresh Goldens/demo-export.md.");
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
