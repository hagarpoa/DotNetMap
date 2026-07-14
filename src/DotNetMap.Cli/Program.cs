using System.CommandLine;
using DotNetMap.Cli.Mcp;
using DotNetMap.Core.Analysis;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

var dbOption = new Option<FileInfo>("--db")
{
    Description = "Path to the DotNetMap SQLite database (default: .dotnetmap/index.db)."
};
dbOption.DefaultValueFactory = _ => new FileInfo(Path.Combine(".dotnetmap", "index.db"));

var formatOption = new Option<string>("--format")
{
    Description = "Output format: md | json (default: md)."
};
formatOption.DefaultValueFactory = _ => "md";

var detailOption = new Option<string>("--detail")
{
    Description = "Output detail: compact (default, low tokens) | full."
};
detailOption.DefaultValueFactory = _ => "compact";

var root = new RootCommand("DotNetMap — local semantic map of a .NET solution for AI context.");

// --- status ---
var statusCheckOpt = new Option<bool>("--check")
{
    Description = "Exit with code 4 if the index is stale vs disk (DNM-009)."
};
var statusVerboseOpt = new Option<bool>("--verbose")
{
    Description = "Include index quality metrics (DNM-022)."
};
var statusCmd = new Command("status", "Show index database status and optional staleness.")
{
    dbOption,
    statusCheckOpt,
    statusVerboseOpt
};
statusCmd.SetAction(parseResult =>
{
    var db = parseResult.GetValue(dbOption)!;
    var check = parseResult.GetValue(statusCheckOpt);
    var verbose = parseResult.GetValue(statusVerboseOpt);
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
        Console.WriteLine($"DotNetMap:    {s.DotNetMapVersion}");

        var stale = IndexStaleness.Check(store);
        Console.WriteLine($"Stale:        {(stale.IsStale ? "YES" : "no")}");
        if (stale.IsStale)
        {
            Console.WriteLine($"  Stale projects: {stale.StaleProjects.Count} ({string.Join(", ", stale.StaleProjects)})");
            if (stale.MissingFiles.Count > 0)
                Console.WriteLine($"  Missing files: {stale.MissingFiles.Count}");
            if (stale.NewFiles.Count > 0)
                Console.WriteLine($"  New files:     {stale.NewFiles.Count}");
            foreach (var d in stale.Details.Take(10))
                Console.WriteLine($"  - {d}");
            if (stale.Details.Count > 10)
                Console.WriteLine($"  - … +{stale.Details.Count - 10} more");
            Console.WriteLine("  Hint: run  index <path> --changed-only");
        }

        if (verbose)
        {
            Console.WriteLine();
            Console.Write(IndexQuality.FormatMarkdown(IndexQuality.Compute(store)));
        }

        if (check && stale.IsStale)
            return 4;
        return 0;
    }
});

// --- doctor ---
var doctorCmd = new Command("doctor", "Check environment + index health (MSBuild, DB, stale, quality).")
{
    dbOption,
    formatOption
};
doctorCmd.SetAction(parseResult =>
{
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var report = Doctor.Run(db.FullName);
    var body = format == "json" ? Doctor.FormatJson(report) : Doctor.FormatMarkdown(report);
    Console.WriteLine(body);
    return report.Ok ? 0 : 1;
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
var includeExternalCalls = new Option<bool>("--include-external-calls")
{
    Description = "Index calls into BCL/NuGet (default: solution-local calls only)."
};
var includeExternalSigDeps = new Option<bool>("--include-external-signature-deps")
{
    Description = "Index member signature type deps from BCL/NuGet (default: solution types only)."
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
    forceFull,
    includeExternalCalls,
    includeExternalSigDeps
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
    var extCalls = parseResult.GetValue(includeExternalCalls);
    var extSig = parseResult.GetValue(includeExternalSigDeps);

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
            IncludeExternalCalls = extCalls,
            IncludeExternalSignatureDeps = extSig,
            ChangedOnly = incremental,
            PreviousMap = previous,
            Progress = new Progress<string>(msg => Console.Error.WriteLine($"  {msg}"))
        };

        Console.Error.WriteLine(incremental ? "DotNetMap index (--changed-only)" : "DotNetMap index");
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
                Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
                return 2;
            }

            var map = store.LoadMapSkeleton();
            if (map is null)
            {
                Console.Error.WriteLine("Could not load map skeleton.");
                return 1;
            }

            var scopes = RelationScope.ParseMany(scopesRaw);
            Console.Error.WriteLine($"DotNetMap consumers ({string.Join(", ", scopes)})");

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
    detailOption,
    outOption,
    includeMembersOpt,
    maxTypesOpt
};
exportCmd.SetAction(parseResult =>
{
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var detail = ParseDetail(parseResult.GetValue(detailOption));
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
            Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
            return 2;
        }

        var exportOpts = new ExportOptions
        {
            IncludeMembers = members,
            MaxTypes = maxTypes < 1 ? 200 : maxTypes,
            IncludeDeps = true,
            Detail = detail
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
    detailOption,
    outOption
};
queryCmd.SetAction(parseResult =>
{
    var text = parseResult.GetValue(queryArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var kind = (parseResult.GetValue(kindOption) ?? "all").ToLowerInvariant();
    var max = parseResult.GetValue(maxOption);
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var detail = ParseDetail(parseResult.GetValue(detailOption));
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
            Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
            return 2;
        }

        var hits = store.Search(text, kind, max);
        var qOpts = new ExportOptions { Detail = detail };
        var body = format switch
        {
            "json" => CompactExporter.SearchToJson(hits, text, qOpts),
            "md" or "markdown" => CompactExporter.SearchToMarkdown(hits, text, qOpts),
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

// --- get (type or method) ---
var nameArg = new Argument<string>("name")
{
    Description = "Type or method: Full.Name, Type.Method, method:Id, or short name."
};
var getKindOption = new Option<string>("--kind")
{
    Description = "auto | type | member (default: auto)."
};
getKindOption.DefaultValueFactory = _ => "auto";

var snippetOpt = new Option<bool>("--snippet")
{
    Description = "Include on-demand source snippet (reads file from disk; not stored in DB)."
};
var contextOpt = new Option<int>("--context")
{
    Description = "Extra context lines around the span when using --snippet (default 0)."
};
contextOpt.DefaultValueFactory = _ => 0;

var getCmd = new Command("get", "Get one type or method (lines, calls, AI-compact).")
{
    nameArg,
    dbOption,
    formatOption,
    detailOption,
    outOption,
    getKindOption,
    snippetOpt,
    contextOpt
};
getCmd.SetAction(parseResult =>
{
    var name = parseResult.GetValue(nameArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var detail = ParseDetail(parseResult.GetValue(detailOption));
    var outFile = parseResult.GetValue(outOption);
    var kind = (parseResult.GetValue(getKindOption) ?? "auto").ToLowerInvariant();
    var wantSnippet = parseResult.GetValue(snippetOpt);
    var context = parseResult.GetValue(contextOpt);

    if (!TryOpenStore(db, out var store, out var err))
    {
        Console.Error.WriteLine(err);
        return 1;
    }

    using (store)
    {
        if (!store!.HasSolutionData())
        {
            Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
            return 2;
        }

        var status = store.GetStatus();
        var getOpts = new ExportOptions
        {
            Detail = detail,
            IncludeSnippet = wantSnippet,
            SnippetContextLines = context,
            SolutionPath = status.SolutionPath,
            ResolveAbsolutePath = store.ResolveFileAbsolutePath
        };

        var preferMember = kind is "member"
            || name.StartsWith("method:", StringComparison.OrdinalIgnoreCase)
            || name.Contains('(', StringComparison.Ordinal);

        if (kind is not "member" && !preferMember)
        {
            var typeDetail = store.GetTypeDetail(name);
            if (typeDetail is not null)
            {
                var body = format switch
                {
                    "json" => CompactExporter.TypeDetailToJson(typeDetail, getOpts),
                    "md" or "markdown" => CompactExporter.TypeDetailToMarkdown(typeDetail, getOpts),
                    _ => throw new InvalidOperationException($"Unknown format: {format}")
                };
                return WriteOutput(body, outFile);
            }

            if (kind is "type")
            {
                Console.Error.WriteLine($"Type not found: {name}");
                return 3;
            }
        }

        var member = store.GetMemberDetail(name);
        if (member is null)
        {
            Console.Error.WriteLine($"Not found (type or member): {name}");
            return 3;
        }

        var memberBody = format switch
        {
            "json" => CompactExporter.MemberDetailToJson(member, getOpts),
            "md" or "markdown" => CompactExporter.MemberDetailToMarkdown(member, getOpts),
            _ => throw new InvalidOperationException($"Unknown format: {format}")
        };
        return WriteOutput(memberBody, outFile);
    }
});

// --- snippet (source only) ---
var snippetNameArg = new Argument<string>("name")
{
    Description = "Type or method name (uses indexed spans)."
};
var snippetCmd = new Command("snippet", "Read source snippet for a type/method from disk (on-demand).")
{
    snippetNameArg,
    dbOption,
    formatOption,
    contextOpt,
    outOption
};
snippetCmd.SetAction(parseResult =>
{
    var name = parseResult.GetValue(snippetNameArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var context = parseResult.GetValue(contextOpt);
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
            Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
            return 2;
        }

        var status = store.GetStatus();
        string? rel = null;
        int? start = null;
        int? end = null;

        var member = store.GetMemberDetail(name);
        if (member is not null)
        {
            rel = member.RelativePath;
            start = member.StartLine;
            end = member.EndLine;
        }
        else
        {
            var type = store.GetTypeDetail(name);
            if (type is null)
            {
                Console.Error.WriteLine($"Not found: {name}");
                return 3;
            }
            rel = type.RelativePath;
            start = type.StartLine;
            end = type.EndLine;
        }

        try
        {
            var snip = DotNetMap.Core.Source.SourceSnippetReader.TryRead(
                rel, start, end,
                new DotNetMap.Core.Source.SourceSnippetOptions
                {
                    SolutionPath = status.SolutionPath,
                    AbsolutePathHint = store.ResolveFileAbsolutePath(rel),
                    ContextLines = context,
                    MaxChars = 4_000
                });

            if (snip is null)
            {
                Console.Error.WriteLine("Snippet unavailable (missing file or lines).");
                return 3;
            }

            var body = format switch
            {
                "json" => CompactExporter.SnippetOnlyJson(snip),
                "md" or "markdown" => CompactExporter.SnippetOnlyMarkdown(snip),
                _ => throw new InvalidOperationException($"Unknown format: {format}")
            };
            return WriteOutput(body, outFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
});

// --- callers (on-demand SymbolFinder) ---
var callersArg = new Argument<string>("member")
{
    Description = "Method, property, or field: Name, Type.Member, method:/property:/field:Id."
};
var callersUpdate = new Option<bool>("--update")
{
    Description = "Persist reference sites into members.consumers_json."
};

var callersCmd = new Command("callers", "Find reference sites for a method/property/field (SymbolFinder, on-demand).")
{
    callersArg,
    dbOption,
    formatOption,
    detailOption,
    outOption,
    callersUpdate
};
callersCmd.SetAction(async (parseResult, ct) =>
{
    var methodName = parseResult.GetValue(callersArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var detail = ParseDetail(parseResult.GetValue(detailOption));
    var outFile = parseResult.GetValue(outOption);
    var update = parseResult.GetValue(callersUpdate);

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
                Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
                return 2;
            }

            Console.Error.WriteLine($"Finding callers of {methodName}...");
            var result = await ImpactAnalysis.GetCallersAsync(store, methodName, update, cancellationToken: ct)
                .ConfigureAwait(false);
            if (update)
                Console.Error.WriteLine($"Updated consumers_json ({result.Callers.Count} callers).");

            var body = ImpactAnalysis.FormatCallers(result, format, detail);
            return WriteOutput(body, outFile);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
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

// --- implementations ---
var implArg = new Argument<string>("type")
{
    Description = "Interface or base type name."
};
var implCmd = new Command("implementations", "Find types that implement/extend a type (SymbolFinder).")
{
    implArg,
    dbOption,
    formatOption,
    outOption
};
implCmd.SetAction(async (parseResult, ct) =>
{
    var typeName = parseResult.GetValue(implArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var outFile = parseResult.GetValue(outOption);

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
                Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
                return 2;
            }

            Console.Error.WriteLine($"Finding implementations of {typeName}...");
            var result = await HierarchyQueries.FindImplementationsAsync(store, typeName, cancellationToken: ct)
                .ConfigureAwait(false);
            return WriteOutput(HierarchyQueries.FormatImplementations(result, format), outFile);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
});

// --- overrides ---
var ovArg = new Argument<string>("method")
{
    Description = "Virtual/abstract method name or Type.Method."
};
var ovCmd = new Command("overrides", "Find methods that override a virtual/abstract method.")
{
    ovArg,
    dbOption,
    formatOption,
    outOption
};
ovCmd.SetAction(async (parseResult, ct) =>
{
    var methodName = parseResult.GetValue(ovArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var outFile = parseResult.GetValue(outOption);

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
                Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
                return 2;
            }

            Console.Error.WriteLine($"Finding overrides of {methodName}...");
            var result = await HierarchyQueries.FindOverridesAsync(store, methodName, cancellationToken: ct)
                .ConfigureAwait(false);
            return WriteOutput(HierarchyQueries.FormatOverrides(result, format), outFile);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
});

// --- impact (multi-hop) ---
var impactArg = new Argument<string>("symbol")
{
    Description = "Type or member name to analyze."
};
var impactDepth = new Option<int>("--depth")
{
    Description = "Max hops (default 2, max 4)."
};
impactDepth.DefaultValueFactory = _ => 2;
var impactMax = new Option<int>("--max-nodes")
{
    Description = "Max nodes in the graph (default 40)."
};
impactMax.DefaultValueFactory = _ => 40;
var impactDir = new Option<string>("--direction")
{
    Description = "both | in | out (default both)."
};
impactDir.DefaultValueFactory = _ => "both";
var impactNoLive = new Option<bool>("--no-live")
{
    Description = "Index edges only (skip SymbolFinder on hop 0)."
};

var impactCmd = new Command("impact", "Compact multi-hop impact graph (callers/deps/consumers).")
{
    impactArg,
    dbOption,
    formatOption,
    impactDepth,
    impactMax,
    impactDir,
    impactNoLive,
    outOption
};
impactCmd.SetAction(async (parseResult, ct) =>
{
    var symbol = parseResult.GetValue(impactArg)!;
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var depth = parseResult.GetValue(impactDepth);
    var maxNodes = parseResult.GetValue(impactMax);
    var dirRaw = (parseResult.GetValue(impactDir) ?? "both").ToLowerInvariant();
    var noLive = parseResult.GetValue(impactNoLive);
    var outFile = parseResult.GetValue(outOption);

    var direction = dirRaw switch
    {
        "in" or "inbound" => ImpactGraph.Direction.Inbound,
        "out" or "outbound" => ImpactGraph.Direction.Outbound,
        _ => ImpactGraph.Direction.Both
    };

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
                Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
                return 2;
            }

            Console.Error.WriteLine($"Building impact graph for {symbol} (depth={depth})...");
            var result = await ImpactGraph.BuildAsync(
                store, symbol, depth, maxNodes, direction, liveHop0: !noLive, ct).ConfigureAwait(false);
            return WriteOutput(ImpactGraph.Format(result, format), outFile);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
});

// --- hotspots ---
var hotMetric = new Option<string>("--by")
{
    Description = "Metric: size (default) | calls | fanin | types."
};
hotMetric.DefaultValueFactory = _ => "size";
var hotLimit = new Option<int>("--top")
{
    Description = "How many results (default 15, max 50)."
};
hotLimit.DefaultValueFactory = _ => 15;

var hotspotsCmd = new Command("hotspots", "List hotspots from the index (size, calls, fan-in, types).")
{
    dbOption,
    formatOption,
    hotMetric,
    hotLimit,
    outOption
};
hotspotsCmd.SetAction(parseResult =>
{
    var db = parseResult.GetValue(dbOption)!;
    var format = (parseResult.GetValue(formatOption) ?? "md").ToLowerInvariant();
    var metric = Hotspots.ParseMetric(parseResult.GetValue(hotMetric));
    var top = parseResult.GetValue(hotLimit);
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
            Console.Error.WriteLine("Database is empty. Run: dotnetmap index <path>");
            return 2;
        }

        var result = Hotspots.Compute(store, metric, top);
        return WriteOutput(Hotspots.Format(result, format), outFile);
    }
});

root.Subcommands.Add(indexCmd);
root.Subcommands.Add(statusCmd);
root.Subcommands.Add(doctorCmd);
root.Subcommands.Add(exportCmd);
root.Subcommands.Add(queryCmd);
root.Subcommands.Add(getCmd);
root.Subcommands.Add(snippetCmd);
root.Subcommands.Add(callersCmd);
root.Subcommands.Add(consumersCmd);
root.Subcommands.Add(implCmd);
root.Subcommands.Add(ovCmd);
root.Subcommands.Add(impactCmd);
root.Subcommands.Add(hotspotsCmd);
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

static DetailLevel ParseDetail(string? value) =>
    (value ?? "compact").Trim().ToLowerInvariant() switch
    {
        "full" => DetailLevel.Full,
        "compact" or "min" or "minimal" => DetailLevel.Compact,
        _ => DetailLevel.Compact
    };

static bool TryOpenStore(FileInfo db, out MapStore? store, out string? error)
{
    store = null;
    error = null;
    if (!db.Exists)
    {
        error = $"No database at {db.FullName}. Run: dotnetmap index <solution>";
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
