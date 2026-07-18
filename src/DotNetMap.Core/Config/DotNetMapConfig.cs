using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Extraction;

namespace DotNetMap.Core.Config;

/// <summary>
/// Project config file (<c>.dotnetmap.json</c> or <c>dotnetmap.json</c>). DNM-027.
/// CLI flags that are set always win; unset flags use config then hard defaults.
/// </summary>
public sealed class DotNetMapConfig
{
    public const string PrimaryFileName = ".dotnetmap.json";
    public const string AlternateFileName = "dotnetmap.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Path to the file this instance was loaded from (absolute), if any.</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    /// <summary>Directory containing the config file (absolute).</summary>
    [JsonIgnore]
    public string? SourceDirectory { get; set; }

    /// <summary>Default SQLite path (relative to config dir or absolute).</summary>
    [JsonPropertyName("db")]
    public string? Database { get; set; }

    [JsonPropertyName("includePrivate")]
    public bool? IncludePrivate { get; set; }

    [JsonPropertyName("includeTest")]
    public bool? IncludeTest { get; set; }

    [JsonPropertyName("includeExternalCalls")]
    public bool? IncludeExternalCalls { get; set; }

    [JsonPropertyName("includeExternalSignatureDeps")]
    public bool? IncludeExternalSignatureDeps { get; set; }

    [JsonPropertyName("fullRelations")]
    public bool? FullRelations { get; set; }

    /// <summary>Scoped consumers specs, e.g. <c>type:IOrderService</c>.</summary>
    [JsonPropertyName("relations")]
    public string[]? Relations { get; set; }

    /// <summary>
    /// Project name substrings or simple globs (<c>*</c> only) to skip.
    /// Example: <c>["*.Tests", "Benchmarks"]</c>.
    /// </summary>
    [JsonPropertyName("excludeProjects")]
    public string[]? ExcludeProjects { get; set; }

    /// <summary>Cap outbound calls stored per method (default 30).</summary>
    [JsonPropertyName("maxCalls")]
    public int? MaxCalls { get; set; }

    /// <summary>Index source body into FTS for <c>query --body</c> (DNM-013). Default false.</summary>
    [JsonPropertyName("indexBody")]
    public bool? IndexBody { get; set; }

    public static DotNetMapConfig Empty { get; } = new();

    /// <summary>Load from an explicit path. Throws on I/O or parse errors.</summary>
    public static DotNetMapConfig Load(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Config file not found: {full}", full);

        var json = File.ReadAllText(full);
        var cfg = JsonSerializer.Deserialize<DotNetMapConfig>(json, JsonOpts) ?? new DotNetMapConfig();
        cfg.SourcePath = full;
        cfg.SourceDirectory = Path.GetDirectoryName(full);
        return cfg;
    }

    /// <summary>
    /// Find <c>.dotnetmap.json</c> / <c>dotnetmap.json</c> walking from <paramref name="startDirectory"/>
    /// up to the drive root. Optionally also checks next to a solution/project path.
    /// </summary>
    public static DotNetMapConfig? TryDiscover(string? startDirectory = null, string? solutionOrProjectPath = null)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(solutionOrProjectPath))
        {
            var sol = Path.GetFullPath(solutionOrProjectPath);
            var solDir = File.Exists(sol)
                ? Path.GetDirectoryName(sol)
                : (Directory.Exists(sol) ? sol : Path.GetDirectoryName(sol));
            if (!string.IsNullOrEmpty(solDir))
            {
                var near = TryLoadInDirectory(solDir, tried);
                if (near is not null)
                    return near;
            }
        }

        var dir = string.IsNullOrWhiteSpace(startDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(startDirectory);

        while (!string.IsNullOrEmpty(dir))
        {
            var found = TryLoadInDirectory(dir, tried);
            if (found is not null)
                return found;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent;
        }

        return null;
    }

    private static DotNetMapConfig? TryLoadInDirectory(string directory, HashSet<string> tried)
    {
        foreach (var name in new[] { PrimaryFileName, AlternateFileName })
        {
            var path = Path.Combine(directory, name);
            var full = Path.GetFullPath(path);
            if (!tried.Add(full))
                continue;
            if (!File.Exists(full))
                continue;
            try
            {
                return Load(full);
            }
            catch
            {
                // Malformed config: stop at this directory (do not silently use bad file).
                throw;
            }
        }

        return null;
    }

    /// <summary>Resolve <see cref="Database"/> to an absolute path, or null if unset.</summary>
    public string? ResolveDatabasePath()
    {
        if (string.IsNullOrWhiteSpace(Database))
            return null;

        if (Path.IsPathRooted(Database))
            return Path.GetFullPath(Database);

        var baseDir = SourceDirectory ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDir, Database));
    }

    /// <summary>
    /// Merge config into index options. Explicit CLI values take precedence via the
    /// <paramref name="cli"/> overlay (nullable = use config / default).
    /// </summary>
    public IndexOptions ToIndexOptions(IndexOptionsOverlay cli)
    {
        IReadOnlyList<RelationScope> scopes = [];
        if (cli.RelationScopes is { Count: > 0 })
            scopes = cli.RelationScopes;
        else if (Relations is { Length: > 0 })
            scopes = RelationScope.ParseMany(Relations);

        var maxCalls = cli.MaxCallsPerMethod
                       ?? MaxCalls
                       ?? 30;
        if (maxCalls < 1)
            maxCalls = 1;

        IReadOnlyList<string> excludes = cli.ExcludeProjects is { Count: > 0 }
            ? cli.ExcludeProjects
            : ExcludeProjects ?? Array.Empty<string>();

        return new IndexOptions
        {
            IncludePrivate = cli.IncludePrivate ?? IncludePrivate ?? false,
            IncludeTest = cli.IncludeTest ?? IncludeTest ?? false,
            FullRelations = cli.FullRelations ?? FullRelations ?? false,
            RelationScopes = scopes,
            LightDeps = true,
            IncludeExternalCalls = cli.IncludeExternalCalls ?? IncludeExternalCalls ?? false,
            IncludeExternalSignatureDeps = cli.IncludeExternalSignatureDeps ?? IncludeExternalSignatureDeps ?? false,
            ChangedOnly = cli.ChangedOnly ?? false,
            PreviousMap = cli.PreviousMap,
            Progress = cli.Progress,
            MaxCallsPerMethod = maxCalls,
            ExcludeProjectPatterns = excludes,
            IndexBody = cli.IndexBody ?? IndexBody ?? false
        };
    }

    /// <summary>Whether <paramref name="projectName"/> matches any exclude pattern.</summary>
    public static bool MatchesExclude(string projectName, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
            return false;

        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var p = raw.Trim();
            if (p.Contains('*', StringComparison.Ordinal))
            {
                if (GlobMatch(projectName, p))
                    return true;
            }
            else if (projectName.Contains(p, StringComparison.OrdinalIgnoreCase)
                     || projectName.Equals(p, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GlobMatch(string value, string pattern)
    {
        // Simple * glob only (case-insensitive).
        var parts = pattern.Split('*');
        if (parts.Length == 1)
            return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);

        var idx = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
                continue;

            if (i == 0)
            {
                if (!value.StartsWith(part, StringComparison.OrdinalIgnoreCase))
                    return false;
                idx = part.Length;
                continue;
            }

            if (i == parts.Length - 1 && !pattern.EndsWith("*", StringComparison.Ordinal))
            {
                if (!value.EndsWith(part, StringComparison.OrdinalIgnoreCase))
                    return false;
                continue;
            }

            var found = value.IndexOf(part, idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return false;
            idx = found + part.Length;
        }

        return true;
    }
}

/// <summary>CLI-supplied overrides for <see cref="DotNetMapConfig.ToIndexOptions"/>.</summary>
public sealed class IndexOptionsOverlay
{
    public bool? IncludePrivate { get; init; }
    public bool? IncludeTest { get; init; }
    public bool? FullRelations { get; init; }
    public bool? IncludeExternalCalls { get; init; }
    public bool? IncludeExternalSignatureDeps { get; init; }
    public bool? ChangedOnly { get; init; }
    public bool? IndexBody { get; init; }
    public IReadOnlyList<RelationScope>? RelationScopes { get; init; }
    public IReadOnlyList<string>? ExcludeProjects { get; init; }
    public int? MaxCallsPerMethod { get; init; }
    public SolutionMap? PreviousMap { get; init; }
    public IProgress<string>? Progress { get; init; }
}
