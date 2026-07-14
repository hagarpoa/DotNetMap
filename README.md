# NetMap

**Local, AI-token-aware semantic map of a .NET solution** — CLI + MCP (soon).

> Index Solution → Project → Namespace → Type → Members with source position, size, XML summary, and light dependencies. Built for agents that need precise context without blowing the token window.

## Positioning

NetMap is **not** a full call-graph engine by default.  
It is a **structure + light-deps** index with optional deep relations on demand.

| Default | Optional |
|---------|----------|
| Types, members, spans, summaries | `--full-relations` / scoped consumers |
| inherits / implements / signature types | SymbolFinder consumers |
| Compact MD/JSON export | Snippets, full relations |

## Status

- [x] Solution, Core, Cli, Tests
- [x] SQLite schema v0 + `MapStore`
- [x] CLI: `index`, `status`, `export`, `query`, `get`, `consumers`, `serve-mcp`
- [x] Roslyn structural extraction + light deps
- [x] FTS5 search + type detail (PR-3)
- [x] Scoped consumers / SymbolFinder (PR-4)
- [x] MCP server stdio (PR-5)
- [x] Incremental (`--changed-only`) + `dotnet tool` pack (PR-6)

See [docs/MVP_CHECKLIST.md](docs/MVP_CHECKLIST.md) and [docs/DECISIONS.md](docs/DECISIONS.md).

## Quickstart

```powershell
cd C:\Users\Cesar\source\repos\NetMap
dotnet build
dotnet test
dotnet run --project src/NetMap.Cli -- index samples/DemoSolution --db .netmap/index.db
dotnet run --project src/NetMap.Cli -- status --db .netmap/index.db
dotnet run --project src/NetMap.Cli -- query Order --db .netmap/index.db
dotnet run --project src/NetMap.Cli -- get OrderService --db .netmap/index.db
dotnet run --project src/NetMap.Cli -- export --members --format md --db .netmap/index.db
```

## Layout

```text
NetMap/
  schema/v0.sql          # SQLite schema (embedded in Core)
  docs/                  # Decisions, MVP checklist
  src/NetMap.Core        # Domain + Store + (soon) Roslyn
  src/NetMap.Cli         # System.CommandLine entry
  tests/NetMap.Tests
  samples/DemoSolution   # Small multi-project sample
```

## CLI

```text
netmap index <path> [--db .netmap/index.db] [--include-private] [--include-test]
                 [--relations type:Name|project:Name|full] [--full-relations]
                 [--changed-only] [--force]
netmap status [--db ...]
netmap export [--format md|json] [--members] [--max-types N] [--out map.md]
netmap query <text> [--kind all|type|member] [--max N] [--format md|json]
netmap get <TypeName|Full.Name|type:Id> [--format md|json]
netmap consumers type:IOrderService [project:Demo.App] [full] [--db ...]
netmap serve-mcp [--db .netmap/index.db]
```

### MCP (stdio)

```json
{
  "mcpServers": {
    "netmap": {
      "command": "dotnet",
      "args": ["run", "--project", "src/NetMap.Cli", "--", "serve-mcp", "--db", ".netmap/index.db"]
    }
  }
}
```

Tools: `get_status`, `get_overview`, `search`, `get_type`  
Resources: `solution://overview`, `type://{name}`  
Prompts: `architecture_review`, `impact_analysis`

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (path, flags, I/O) |
| 2 | Database empty / not indexed |
| 3 | No matches (`query`) or type not found (`get`) |

## Install as global tool

```powershell
dotnet pack src/NetMap.Cli/NetMap.Cli.csproj -c Release -o artifacts
dotnet tool install -g NetMap.Tool --add-source ./artifacts --version 0.1.0
netmap index ./MySolution --db .netmap/index.db
```

See [docs/RELEASE.md](docs/RELEASE.md) for notes and incremental semantics.

## License

MIT — see [LICENSE](LICENSE).
