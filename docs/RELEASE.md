# DotNetMap 0.2.0 — Release notes

## What it is

Local-first, AI-token-aware semantic map of a .NET solution:

- **CLI** for index / query / export / consumers  
- **MCP** stdio server for agents  
- **SQLite** persistence with FTS5  

## Install (local tool)

```powershell
cd C:\Users\Cesar\source\repos\NetMap
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet tool uninstall -g DotNetMap.Tool  # if reinstalling
dotnet tool install -g DotNetMap.Tool --add-source artifacts --version 0.2.0
dotnetmap --help
```

Or run without installing:

```powershell
dotnet run --project src/DotNetMap.Cli -- --help
```

## Quickstart

```powershell
dotnetmap index path/to/solution.slnx --db .dotnetmap/index.db
dotnetmap status --db .dotnetmap/index.db
dotnetmap query OrderService --db .dotnetmap/index.db
dotnetmap get OrderService --db .dotnetmap/index.db
dotnetmap export --members --out map.md --db .dotnetmap/index.db

# Incremental (project-level)
dotnetmap index path/to/solution.slnx --db .dotnetmap/index.db --changed-only

# Scoped consumers (SymbolFinder)
dotnetmap consumers type:IOrderService --db .dotnetmap/index.db

# MCP stdio
dotnetmap serve-mcp --db .dotnetmap/index.db
```

## Incremental semantics

- Fingerprint = hash(csproj) + sorted hashes of source files (excluding bin/obj/generated/migrations).  
- If **any** file in a project changes → **entire project** is reindexed.  
- Requires same solution path and same `--include-private` / `--include-test` flags.  
- Default index is **full**; pass `--changed-only` for reuse.  

## Included in 0.2.0 (product name DotNetMap)

- Method lines / lineCount / file  
- Outbound method **calls** on index  
- On-demand **callers** (`dotnetmap callers`)  
- Incremental project-level reindex  
- MCP tools including `get_method`  

## Not in 0.2.0

- Method-level incremental (file/project only)  
- Materialized full call graph by default  
- Embeddings / TOON  
- Cross-language support  

Planned work: [BACKLOG.md](BACKLOG.md) (DNM-001+).

## Compatibility

- .NET 10 SDK  
- Windows tested; MSBuildLocator uses installed VS/SDK MSBuild  
