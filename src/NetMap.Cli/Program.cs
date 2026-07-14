using System.CommandLine;
using NetMap.Cli.Mcp;
using NetMap.Core.Domain;
using NetMap.Core.Export;
using NetMap.Core.Extraction;
using NetMap.Core.Store;

var dbOption = new Option<FileInfo>("--db")
{
    Description = "Path to the NetMap SQLite database (default: .netmap/index.db)."
};
dbOption.DefaultValueFactory = _ => new FileInfo(Path.Combine(".netmap", "index.db"));

var formatOption = new Option<string>("--format")
{
    Description = "Output format: md | json (default: md)."
};
formatOption.DefaultValueFactory = _ => "md";

var root = new RootCommand("NetMap — local semantic map of a .NET solution for AI context.");

// --- status ---
var statusCmd = new Command("status", "Show index database status.");
statusCmd.Options.Add(dbOption);
statusCmd.SetAction(parseResult =>
{
    var db = parseResult.GetValue(dbOption)!;
    if (!TryOpenStore(db, out var store, out var err))
    {
        Console.Error.WriteLine(err);
        return 1;
    }

    using (store)
    {
        var s = store!.GetStatus();
        if (!store.HasSolutionData())
        {
            Console.WriteLine($"Database exists but is empty: {db.FullName}");
            return 0;
        }

        Console.WriteLine($"Solution:     {s.SolutionName}");
        Console.WriteLine($"Path:         {s.SolutionPath}");
        Console.WriteLine($"Indexed (UTC):{s.IndexedAtUtc:u}");
        Console.WriteLine($"Mode:         {s.IndexMode}");
        Console.WriteLine($"Schema:       v{s.SchemaVersion}");
        Console.WriteLine($"Projects:     {s.ProjectCount}");
        Console.WriteLine($"Types:        {s.TypeCount}");
        Console.WriteLine($"Members:      {s.MemberCount}");
        Console.WriteLine($"Files:        {s.FileCount}");
        Console.WriteLine($"DB size:      {s.DatabaseBytes:N0} bytes");
        Console.WriteLine($"Token est.:   {s.TokenEstimateOverview}");
        Console.WriteLine($"Private:      {s.IncludePrivate} | Tests: {s.IncludeTest}");
        Console.WriteLine($"NetMap:       {s.NetMapVersion}");
        return 0;
    }
});

// --- index ---
var pathArg = new Argument<string>("path")
{
    Description = "Path to a .sln, .slnx, .csproj, or directory containing one."
};
var includePrivate = new Option<bool>("--include-private")
{
    Description = "Include private members (default: public + internal only)."
};
var includeTest = new Option<bool>("--include-test")
{
    Description = "Include test projects (default: excluded)."
};
var fullRelations = new Option<bool>("--full-relations")
{
    Description = "Find consumers for ALL types via SymbolFinder (slow on large solutions)."
};
var relationsOption = new Option<string[]>("--relations")
{
    Description = "Scoped consumers: type:Name, project:Name, and/or full. Can repeat. Example: --relations type:IOrderService"
};
relationsOption.AllowMultipleArgumentsPerToken = true;
relationsOption.DefaultValueFactory = _ => [];
var changedOnly = new Option<bool>("--changed-only")
{
    Description = "Reuse unchanged projects from existing DB (project-level invalidation)."
};
var forceFull = new Option<bool>("--force")
{
    Description = "Force full reindex (ignore --changed-only)."
};

var indexCmd = new Command("index", "Index a solution into the local SQLite map.")
{
    pathArg,
    dbOption,
    includePrivate,
    includeTest,
    fullRelations,
    relationsOption,
    changedOnly,
    forceFull
};
indexCmd.SetAction(async (parseResult, ct) =>
{
    var path = parseResult.GetValue(pathArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var priv = parseResult.GetValue(includePrivate);
    var tests = parseResult.GetValue(includeTest);
    var full = parseResult.GetValue(fullRelations);
    var relSpecs = parseResult.GetValue(relationsOption) ?? [];
    var incremental = parseResult.GetValue(changedOnly) && !parseResult.GetValue(forceFull);

    try
    {
        IReadOnlyList<RelationScope> scopes = [];
        if (relSpecs.Length > 0)
            scopes = RelationScope.ParseMany(relSpecs);

        SolutionMap? previous = null;
        if (incremental && db.Exists)
        {
            using var existing = MapStore.Open(db.FullName);
            if (existing.HasSolutionData())
                previous = existing.LoadFullMap();
        }

        var options = new IndexOptions
        {
            IncludePrivate = priv,
            IncludeTest = tests,
            FullRelations = full,
            RelationScopes = scopes,
            LightDeps = true,
            ChangedOnly = incremental,
            PreviousMap = previous,
            Progress = new Progress<string>(msg => Console.Error.WriteLine($"  {msg}"))
        };

        Console.Error.WriteLine(incremental ? "NetMap index (--changed-only)" : "NetMap index");
        var indexer = new SolutionIndexer();
        var result = await indexer.IndexAsync(path, options, ct).ConfigureAwait(false);

        using var store = MapStore.Open(db.FullName);
        store.WriteMap(result.Map);

        var status = store.GetStatus();
        Console.WriteLine($"Indexed → {db.FullName}");
        Console.WriteLine($"  solution: {status.SolutionName}");
        Console.WriteLine($"  projects: {status.ProjectCount}, files: {status.FileCount}");
        Console.WriteLine($"  types: {status.TypeCount}, members: {status.MemberCount}");
        Console.WriteLine($"  mode: {status.IndexMode}, token estimate: {status.TokenEstimateOverview}");
        if (incremental || result.ProjectsReused > 0)
            Console.WriteLine($"  incremental: reused {result.ProjectsReused}, reindexed {result.ProjectsReindexed}" +
                              (result.WasIncremental ? "" : " (full pass)"));
        if (result.IncrementalNote is not null)
            Console.WriteLine($"  note: {result.IncrementalNote}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        if (ex.InnerException is not null)
            Console.Error.WriteLine($"  {ex.InnerException.Message}");
        return 1;
    }
});

// --- consumers (on existing index) ---
var consumersScopeArg = new Argument<string[]>("scope")
{
    Description = "One or more scopes: type:Name, project:Name, full"
};
consumersScopeArg.Arity = ArgumentArity.OneOrMore;

var consumersCmd = new Command("consumers", "Find consumers via SymbolFinder and update the index (scoped).")
{
    consumersScopeArg,
    dbOption
};
consumersCmd.SetAction(async (parseResult, ct) =>
{
    var scopesRaw = parseResult.GetValue(consumersScopeArg) ?? [];
    var db = parseResult.GetValue(dbOption)!;

    if (!TryOpenStore(db, out var store, out var err))
    {
        Console.Error.WriteLine(err);
        return 1;
    }

    try
    {
        using (store)
        {
            if (!store!.HasSolutionData())
            {
                Console.Error.WriteLine("Database is empty. Run: netmap index <path>");
                return 2;
            }

            var map = store.LoadMapSkeleton();
            if (map is null)
            {
                Console.Error.WriteLine("Could not load map skeleton.");
                return 1;
            }

            var scopes = RelationScope.ParseMany(scopesRaw);
            Console.Error.WriteLine($"NetMap consumers ({string.Join(", ", scopes)})");

            var indexer = new SolutionIndexer();
            await indexer.ApplyConsumersAsync(map, scopes, ct).ConfigureAwait(false);

            var touched = map.Projects.SelectMany(p => p.Types)
                .Where(t => t.Consumers.Count > 0 || scopes.Any(s => s.Kind == RelationScopeKind.Full
                    || (s.Kind == RelationScopeKind.Type && MatchesType(t, s.Name!))
                    || (s.Kind == RelationScopeKind.Project && map.Projects.Any(p =>
                        p.Types.Contains(t) && (
                            p.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)
                            || p.Id.Equals("project:" + s.Name, StringComparison.OrdinalIgnoreCase))))))
                .ToList();

            // Persist all types that were in scope (even if 0 consumers — clears stale)
            var toSave = ResolveTypesForScopes(map, scopes).ToList();
            store.SaveTypeConsumers(toSave, IndexMode.FullRelations);

            Console.WriteLine($"Updated consumers for {toSave.Count} type(s):");
            foreach (var t in toSave.OrderBy(t => t.FullName))
                Console.WriteLine($"  {t.FullName}: {t.Consumers.Count} consumer(s)");
            return 0;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
});

// --- export ---
var outOption = new Option<FileInfo?>("--out")
{
    Description = "Output file (default: stdout)."
};
var includeMembersOpt = new Option<bool>("--members")
{
    Description = "Include member signatures under each type (larger, more tokens)."
};
var maxTypesOpt = new Option<int>("--max-types")
{
    Description = "Max types in export (default: 200)."
};
maxTypesOpt.DefaultValueFactory = _ => 200;

var exportCmd = new Command("export", "Export a compact map for AI context.")
{
    dbOption,
    formatOption,
    outOption,
    includeMembersOpt,
    maxTypesOpt
};
exportCmd.SetAction(parseResult =>
{
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var outFile = parseResult.GetValue(outOption);
    var members = parseResult.GetValue(includeMembersOpt);
    var maxTypes = parseResult.GetValue(maxTypesOpt);

    if (!TryOpenStore(db, out var store, out var err))
    {
        Console.Error.WriteLine(err);
        return 1;
    }

    using (store)
    {
        if (!store!.HasSolutionData())
        {
            Console.Error.WriteLine("Database is empty. Run: netmap index <path>");
            return 2;
        }

        var exportOpts = new ExportOptions
        {
            IncludeMembers = members,
            MaxTypes = maxTypes < 1 ? 200 : maxTypes,
            IncludeDeps = true
        };

        try
        {
            var text = format switch
            {
                "json" => CompactExporter.ToJson(store, exportOpts),
                "md" or "markdown" => CompactExporter.ToMarkdown(store, exportOpts),
                _ => throw new InvalidOperationException($"Unknown format: {format}. Use md or json.")
            };
            return WriteOutput(text, outFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
});

// --- query ---
var queryArg = new Argument<string>("text")
{
    Description = "Free-text search over type/member names and summaries (FTS5)."
};
var kindOption = new Option<string>("--kind")
{
    Description = "Filter: all | type | member (default: all)."
};
kindOption.DefaultValueFactory = _ => "all";
var maxOption = new Option<int>("--max")
{
    Description = "Max results (default: 20)."
};
maxOption.DefaultValueFactory = _ => 20;

var queryCmd = new Command("query", "Search the index (FTS5 over names + summaries).")
{
    queryArg,
    dbOption,
    kindOption,
    maxOption,
    formatOption,
    outOption
};
queryCmd.SetAction(parseResult =>
{
    var text = parseResult.GetValue(queryArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var kind = (parseResult.GetValue(kindOption) ?? "all").ToLowerInvariant();
    var max = parseResult.GetValue(maxOption);
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var outFile = parseResult.GetValue(outOption);

    if (kind is not ("all" or "type" or "member"))
    {
        Console.Error.WriteLine("error: --kind must be all, type, or member.");
        return 1;
    }

    if (!TryOpenStore(db, out var store, out var err))
    {
        Console.Error.WriteLine(err);
        return 1;
    }

    using (store)
    {
        if (!store!.HasSolutionData())
        {
            Console.Error.WriteLine("Database is empty. Run: netmap index <path>");
            return 2;
        }

        var hits = store.Search(text, kind, max);
        var body = format switch
        {
            "json" => CompactExporter.SearchToJson(hits, text),
            "md" or "markdown" => CompactExporter.SearchToMarkdown(hits, text),
            _ => throw new InvalidOperationException($"Unknown format: {format}")
        };

        if (hits.Count == 0)
        {
            var code = WriteOutput(body, outFile);
            return code != 0 ? code : 3;
        }

        return WriteOutput(body, outFile);
    }
});

// --- get ---
var nameArg = new Argument<string>("name")
{
    Description = "Type full name, short name, or type:Id."
};

var getCmd = new Command("get", "Get one type with members (compact, AI-friendly).")
{
    nameArg,
    dbOption,
    formatOption,
    outOption
};
getCmd.SetAction(parseResult =>
{
    var name = parseResult.GetValue(nameArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var outFile = parseResult.GetValue(outOption);

    if (!TryOpenStore(db, out var store, out var err))
    {
        Console.Error.WriteLine(err);
        return 1;
    }

    using (store)
    {
        if (!store!.HasSolutionData())
        {
            Console.Error.WriteLine("Database is empty. Run: netmap index <path>");
            return 2;
        }

        var detail = store.GetTypeDetail(name);
        if (detail is null)
        {
            Console.Error.WriteLine($"Type not found: {name}");
            return 3;
        }

        var body = format switch
        {
            "json" => CompactExporter.TypeDetailToJson(detail),
            "md" or "markdown" => CompactExporter.TypeDetailToMarkdown(detail),
            _ => throw new InvalidOperationException($"Unknown format: {format}")
        };

        return WriteOutput(body, outFile);
    }
});

// --- serve-mcp ---
var serveCmd = new Command("serve-mcp", "Start MCP server over stdio (for AI agents).")
{
    dbOption
};
serveCmd.SetAction(async (parseResult, ct) =>
{
    var db = parseResult.GetValue(dbOption)!;
    var path = db.FullName;
    if (!db.Exists)
        Console.Error.WriteLine($"warning: database not found yet at {path} (tools will fail until indexed).");

    try
    {
        return await McpHost.RunAsync(path, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
});

root.Subcommands.Add(indexCmd);
root.Subcommands.Add(statusCmd);
root.Subcommands.Add(exportCmd);
root.Subcommands.Add(queryCmd);
root.Subcommands.Add(getCmd);
root.Subcommands.Add(consumersCmd);
root.Subcommands.Add(serveCmd);

return await root.Parse(args).InvokeAsync();

static bool MatchesType(TypeNode t, string name) =>
    t.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)
    || t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
    || t.Id.Equals(name, StringComparison.OrdinalIgnoreCase)
    || t.Id.Equals("type:" + name, StringComparison.OrdinalIgnoreCase);

static IEnumerable<TypeNode> ResolveTypesForScopes(SolutionMap map, IReadOnlyList<RelationScope> scopes)
{
    if (scopes.Any(s => s.Kind == RelationScopeKind.Full))
        return map.Projects.SelectMany(p => p.Types);

    var set = new Dictionary<string, TypeNode>(StringComparer.Ordinal);
    foreach (var scope in scopes)
    {
        if (scope.Kind == RelationScopeKind.Type)
        {
            foreach (var t in map.Projects.SelectMany(p => p.Types))
            {
                if (MatchesType(t, scope.Name!))
                    set[t.Id] = t;
            }
        }
        else if (scope.Kind == RelationScopeKind.Project)
        {
            foreach (var p in map.Projects)
            {
                if (p.Name.Equals(scope.Name, StringComparison.OrdinalIgnoreCase)
                    || p.Id.Equals("project:" + scope.Name, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var t in p.Types)
                        set[t.Id] = t;
                }
            }
        }
    }

    return set.Values;
}

static bool TryOpenStore(FileInfo db, out MapStore? store, out string? error)
{
    store = null;
    error = null;
    if (!db.Exists)
    {
        error = $"No database at {db.FullName}. Run: netmap index <solution>";
        return false;
    }

    try
    {
        store = MapStore.Open(db.FullName);
        return true;
    }
    catch (Exception ex)
    {
        error = $"error opening database: {ex.Message}";
        return false;
    }
}

static int WriteOutput(string text, FileInfo? outFile)
{
    if (outFile is not null)
    {
        var dir = outFile.DirectoryName;
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(outFile.FullName, text);
        Console.WriteLine($"Wrote {outFile.FullName} (~{TokenEstimator.FromText(text)} tokens)");
        return 0;
    }

    Console.WriteLine(text);
    return 0;
}
