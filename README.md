# DotNetMap

**Local, AI-token-aware semantic map of a .NET solution** — CLI + MCP (soon).

> Index Solution → Project → Namespace → Type → Members with source position, size, XML summary, and light dependencies. Built for agents that need precise context without blowing the token window.

## Positioning

DotNetMap is **not** a full call-graph engine by default.  
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
dotnet run --project src/DotNetMap.Cli -- index samples/DemoSolution --db .dotnetmap/index.db
dotnet run --project src/DotNetMap.Cli -- status --db .dotnetmap/index.db
dotnet run --project src/DotNetMap.Cli -- query Order --db .dotnetmap/index.db
dotnet run --project src/DotNetMap.Cli -- get OrderService --db .dotnetmap/index.db
dotnet run --project src/DotNetMap.Cli -- export --members --format md --db .dotnetmap/index.db
```

## Layout

```text
DotNetMap/
  schema/v0.sql          # SQLite schema (embedded in Core)
  docs/                  # Decisions, MVP checklist
  src/DotNetMap.Core        # Domain + Store + (soon) Roslyn
  src/DotNetMap.Cli         # System.CommandLine entry
  tests/DotNetMap.Tests
  samples/DemoSolution   # Small multi-project sample
```

## CLI

```text
dotnetmap index <path> [--db .dotnetmap/index.db] [--include-private] [--include-test]
                 [--relations type:Name|project:Name|full] [--full-relations]
                 [--changed-only] [--force]
dotnetmap status [--db ...]
dotnetmap export [--format md|json] [--members] [--max-types N] [--out map.md]
dotnetmap query <text> [--kind all|type|member] [--max N] [--format md|json]
dotnetmap get <Type|Method|Type.Method> [--kind auto|type|member] [--format md|json]
dotnetmap callers <Method|Type.Method> [--update]   # on-demand SymbolFinder
dotnetmap consumers type:IOrderService [project:Demo.App] [full] [--db ...]
dotnetmap serve-mcp [--db .dotnetmap/index.db]
```

### Method-level AI context

Each method stores **file + Lstart–Lend + lineCount**, **outbound calls** (`kind: calls` in dependencies), and optional **callers** (`consumers_json`, filled via `dotnetmap callers --update`).

```powershell
dotnetmap get OrderService.SaveAsync --db .dotnetmap/index.db
dotnetmap callers CalculateTotal --db .dotnetmap/index.db
```

### MCP (stdio)

```json
{
  "mcpServers": {
    "dotnetmap": {
      "command": "dotnet",
      "args": ["run", "--project", "src/DotNetMap.Cli", "--", "serve-mcp", "--db", ".dotnetmap/index.db"]
    }
  }
}
```

Tools: `get_status`, `get_overview`, `search`, `get_type`, `get_method`  
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
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet tool install -g DotNetMap.Tool --add-source ./artifacts --version 0.2.0
dotnetmap index ./MySolution --db .dotnetmap/index.db
```

See [docs/RELEASE.md](docs/RELEASE.md) for notes and incremental semantics.

## License

MIT — see [LICENSE](LICENSE).
