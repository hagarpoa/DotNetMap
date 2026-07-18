using DotNetMap.Core.Config;
using DotNetMap.Core.Extraction;

namespace DotNetMap.Tests;

public class ConfigTests
{
    [Fact]
    public void Load_ParsesCamelCaseJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dotnetmap-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ".dotnetmap.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "db": ".dotnetmap/custom.db",
                  "includePrivate": true,
                  "includeTest": false,
                  "includeExternalCalls": true,
                  "relations": ["type:IOrderService"],
                  "excludeProjects": ["*.Tests", "Benchmarks"],
                  "maxCalls": 12
                }
                """);

            var cfg = DotNetMapConfig.Load(path);
            Assert.Equal(path, cfg.SourcePath);
            Assert.True(cfg.IncludePrivate);
            Assert.False(cfg.IncludeTest);
            Assert.True(cfg.IncludeExternalCalls);
            Assert.Equal(12, cfg.MaxCalls);
            Assert.Contains("type:IOrderService", cfg.Relations!);
            Assert.Equal(2, cfg.ExcludeProjects!.Length);

            var opts = cfg.ToIndexOptions(new IndexOptionsOverlay());
            Assert.True(opts.IncludePrivate);
            Assert.False(opts.IncludeTest);
            Assert.True(opts.IncludeExternalCalls);
            Assert.Equal(12, opts.MaxCallsPerMethod);
            Assert.Single(opts.RelationScopes);
            Assert.Equal(2, opts.ExcludeProjectPatterns.Count);
            Assert.EndsWith(Path.Combine(".dotnetmap", "custom.db"), cfg.ResolveDatabasePath()!,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ToIndexOptions_CliOverridesConfig()
    {
        var cfg = new DotNetMapConfig
        {
            IncludePrivate = true,
            IncludeTest = true,
            MaxCalls = 5,
            ExcludeProjects = ["A"]
        };

        var opts = cfg.ToIndexOptions(new IndexOptionsOverlay
        {
            IncludePrivate = false,
            IncludeTest = false,
            MaxCallsPerMethod = 99,
            ExcludeProjects = ["B", "C"]
        });

        Assert.False(opts.IncludePrivate);
        Assert.False(opts.IncludeTest);
        Assert.Equal(99, opts.MaxCallsPerMethod);
        Assert.Equal(["B", "C"], opts.ExcludeProjectPatterns);
    }

    [Fact]
    public void TryDiscover_FindsConfigWalkingUp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnetmap-disc-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(nested);
        var cfgPath = Path.Combine(root, "dotnetmap.json");
        try
        {
            File.WriteAllText(cfgPath, """{ "includePrivate": true }""");
            var found = DotNetMapConfig.TryDiscover(nested);
            Assert.NotNull(found);
            Assert.True(found!.IncludePrivate);
            Assert.Equal(Path.GetFullPath(cfgPath), found.SourcePath);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void MatchesExclude_SubstringAndGlob()
    {
        Assert.True(DotNetMapConfig.MatchesExclude("Demo.Tests", ["*.Tests"]));
        Assert.True(DotNetMapConfig.MatchesExclude("MyBenchmarks", ["Benchmarks"]));
        Assert.False(DotNetMapConfig.MatchesExclude("Demo.App", ["*.Tests", "Benchmarks"]));
        Assert.True(DotNetMapConfig.MatchesExclude("Foo.Bar.Tests", ["*.Tests"]));
    }
}
