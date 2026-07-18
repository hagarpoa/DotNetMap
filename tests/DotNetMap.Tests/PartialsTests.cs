using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

/// <summary>DNM-016 — partial type locations across multiple files.</summary>
public class PartialsTests
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
    public async Task PartialOrder_HasTwoLocations_InMapAndGetType()
    {
        var demo = FindDemoSolution();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            IncludePrivate = false
        });

        var order = result.Map.Projects.SelectMany(p => p.Types)
            .First(t => t.FullName.EndsWith(".Order", StringComparison.Ordinal)
                        && !t.FullName.Contains("OrderLine", StringComparison.Ordinal)
                        && !t.FullName.Contains("OrderService", StringComparison.Ordinal)
                        && !t.FullName.Contains("OrderCalculator", StringComparison.Ordinal));

        Assert.True(order.Locations.Count >= 2,
            $"Expected ≥2 partial locations, got {order.Locations.Count}: " +
            string.Join(", ", order.Locations.Select(l => l.RelativePath)));

        var paths = order.Locations
            .Select(l => l.RelativePath?.Replace('\\', '/') ?? "")
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(paths, p => p.EndsWith("Order.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, p => p.Contains("Order.Pricing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(order.Locations, l => l.IsPrimary);

        // Members from both files are merged
        Assert.Contains(order.Members, m => m.Name == "GetSubtotal");
        Assert.Contains(order.Members, m => m.Name is "Id" or "CustomerName" or "Lines");

        var dbPath = Path.Combine(Path.GetTempPath(), $"dotnetmap-partials-{Guid.NewGuid():N}.db");
        try
        {
            using var store = MapStore.Open(dbPath);
            store.WriteMap(result.Map);

            var detail = store.GetTypeDetail("Order");
            Assert.NotNull(detail);
            Assert.True(detail!.AllLocations.Count >= 2);

            var md = CompactExporter.TypeDetailToMarkdown(detail);
            Assert.Contains("partials", md, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("locations", CompactExporter.TypeDetailToJson(detail), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-wal"); } catch { /* ignore */ }
            try { File.Delete(dbPath + "-shm"); } catch { /* ignore */ }
        }
    }
}
