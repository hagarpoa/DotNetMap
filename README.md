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

See [docs/MVP_CHECKLIST.md](docs/MVP_CHECKLIST.md), [docs/DECISIONS.md](docs/DECISIONS.md), [docs/AGENT_PLAYBOOK.md](docs/AGENT_PLAYBOOK.md) (agent refactor workflows), and [docs/BACKLOG.md](docs/BACKLOG.md).

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
dotnetmap index <path> [--db .dotnetmap/index.db] [--config .dotnetmap.json]
                 [--include-private] [--include-test] [--include-generated] [--index-body]
                 [--relations type:Name|project:Name|full] [--full-relations]
                 [--exclude-project pattern] [--max-calls N]
                 [--changed-only] [--force]
                 [--include-external-calls] [--include-external-signature-deps]
dotnetmap status [--db ...] [--config ...] [--check] [--verbose]
dotnetmap doctor [--db ...] [--config ...]
dotnetmap export [--format md|json] [--members] [--max-types N] [--out map.md]
dotnetmap query <text> [--kind all|type|member] [--body] [--max N] [--format md|json]
dotnetmap get <Type|Method|Type.Method> [--kind auto|type|member] [--format md|json] [--detail compact|full] [--snippet] [--context N]
dotnetmap snippet <Type|Method> [--context N]
dotnetmap callers <Method|Property|Field|Type.Member> [--update]
dotnetmap consumers type:IOrderService [project:Demo.App] [full] [--db ...]
dotnetmap implementations <Type|Interface>
dotnetmap overrides <Method|Type.Method>
dotnetmap impact <Type|Member> [--depth 2] [--max-nodes 40] [--direction both|in|out] [--no-live]
dotnetmap hotspots [--by size|calls|fanin|types] [--top 15]
dotnetmap serve-mcp [--db .dotnetmap/index.db] [--config ...]
```

### Config file (DNM-027)

Optional `.dotnetmap.json` or `dotnetmap.json` (walks up from cwd / solution path). Explicit CLI flags always win.

```json
{
  "db": ".dotnetmap/index.db",
  "includePrivate": false,
  "includeTest": false,
  "includeExternalCalls": false,
  "includeExternalSignatureDeps": false,
  "indexBody": false,
  "includeGenerated": false,
  "relations": ["type:IOrderService"],
  "excludeProjects": ["*.Tests", "Benchmarks"],
  "maxCalls": 30
}
```

### Body search (DNM-013)

Optional full-text over source files (off by default — heavier index):

```powershell
dotnetmap index samples/DemoSolution --index-body --db .dotnetmap/index.db
dotnetmap query TODO --body --db .dotnetmap/index.db
# → [body] `Demo.App/OrderService.cs:L19` — `// TODO: ...`
```

### Token-aware output (DNM-006 / DNM-007)

| Default | Opt-in |
|---------|--------|
| `--detail compact` | `--detail full` (signature deps, more fields) |
| Calls = **solution-local only** | `--include-external-calls` on index |
| Signature BCL types omitted from index | `--include-external-signature-deps` |
| Caps ~12k chars / 20 relations shown | counts + truncated flag |

### Method-level AI context

Each method stores **file + Lstart–Lend + lineCount**, **outbound calls** (`kind: calls`), and optional **callers** (`consumers_json` via `dotnetmap callers --update`).

```powershell
dotnetmap get OrderService.SaveAsync --db .dotnetmap/index.db
dotnetmap get OrderService.SaveAsync --snippet --db .dotnetmap/index.db
dotnetmap callers CalculateTotal --db .dotnetmap/index.db
# → Sites with file:line, e.g. SaveAsync @ Demo.App/OrderService.cs:L18
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

Tools: `get_status` (+ quality), `doctor`, `get_overview`, `search`, `get_type`, `get_method`, `get_snippet`, `get_callers`, `get_consumers`, `find_implementations`, `find_overrides`, `get_impact`, `get_hotspots`  
Resources: `solution://overview`, `type://{name}`  
Prompts: `architecture_review`, `impact_analysis`, `refactor_plan`

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (path, flags, I/O) |
| 2 | Database empty / not indexed |
| 3 | No matches (`query`) or type not found (`get`) |
| 4 | `status --check` and index is **stale** vs disk |

## Install as global tool

```powershell
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet tool install -g DotNetMap.Tool --add-source ./artifacts --version 0.3.0
dotnetmap index ./MySolution --db .dotnetmap/index.db
```

See [docs/RELEASE.md](docs/RELEASE.md) for notes and incremental semantics.

## License

MIT — see [LICENSE](LICENSE).
