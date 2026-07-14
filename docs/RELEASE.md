# NetMap 0.1.0 — Release notes

## What it is

Local-first, AI-token-aware semantic map of a .NET solution:

- **CLI** for index / query / export / consumers  
- **MCP** stdio server for agents  
- **SQLite** persistence with FTS5  

## Install (local tool)

```powershell
cd C:\Users\Cesar\source\repos\NetMap
dotnet pack src/NetMap.Cli/NetMap.Cli.csproj -c Release -o artifacts
dotnet tool uninstall -g NetMap.Tool  # if reinstalling
dotnet tool install -g NetMap.Tool --add-source artifacts --version 0.1.0
netmap --help
```

Or run without installing:

```powershell
dotnet run --project src/NetMap.Cli -- --help
```

## Quickstart

```powershell
netmap index path/to/solution.slnx --db .netmap/index.db
netmap status --db .netmap/index.db
netmap query OrderService --db .netmap/index.db
netmap get OrderService --db .netmap/index.db
netmap export --members --out map.md --db .netmap/index.db

# Incremental (project-level)
netmap index path/to/solution.slnx --db .netmap/index.db --changed-only

# Scoped consumers (SymbolFinder)
netmap consumers type:IOrderService --db .netmap/index.db

# MCP stdio
netmap serve-mcp --db .netmap/index.db
```

## Incremental semantics

- Fingerprint = hash(csproj) + sorted hashes of source files (excluding bin/obj/generated/migrations).  
- If **any** file in a project changes → **entire project** is reindexed.  
- Requires same solution path and same `--include-private` / `--include-test` flags.  
- Default index is **full**; pass `--changed-only` for reuse.  

## Not in 0.1.0

- Method-level incremental  
- Materialized full call graph by default  
- Embeddings / TOON  
- Cross-language support  

## Compatibility

- .NET 10 SDK  
- Windows tested; MSBuildLocator uses installed VS/SDK MSBuild  
