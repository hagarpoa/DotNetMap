# DotNetMap

**Local, AI-token-aware semantic map of a .NET solution** — CLI + MCP (stdio).

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

**1.0.0** — local AI map ready for agent workflows.

- [x] CLI + MCP (impact, snippets, hotspots, doctor, body FTS, edges)
- [x] Sample language surface + multi-TFM + `.sln`/`.slnx`
- [x] Security allowlist, troubleshooting, CHANGELOG / SECURITY
- [x] `dotnet tool` pack (`DotNetMap.Tool`)

**Docs:** [MCP_FOR_AGENTS.md](docs/MCP_FOR_AGENTS.md) (paste into AI context) · [AGENT_PLAYBOOK.md](docs/AGENT_PLAYBOOK.md) · [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) · [RELEASE.md](docs/RELEASE.md) · [DECISIONS.md](docs/DECISIONS.md) · [BACKLOG.md](docs/BACKLOG.md) · [SECURITY.md](SECURITY.md) · [CHANGELOG.md](CHANGELOG.md)

## Quickstart

```powershell
cd C:\Projects\AI\NetMap
dotnet build
dotnet test
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet tool install -g DotNetMap.Tool --add-source artifacts --version 1.0.0
dotnetmap index samples/DemoSolution --db .dotnetmap/index.db
dotnetmap status --verbose --db .dotnetmap/index.db
dotnetmap get Money --db .dotnetmap/index.db
```

## Layout

```text
DotNetMap/
  schema/v0.sql             # SQLite schema (embedded; edges + FTS)
  docs/                     # Agent MCP guide, playbook, release, backlog
  src/DotNetMap.Core        # Domain, Store, Roslyn extraction, analysis
  src/DotNetMap.Cli         # CLI + MCP server (dotnetmap)
  tests/DotNetMap.Tests
  samples/DemoSolution      # Language surface + multi-TFM sample
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

Full agent reference: **[docs/MCP_FOR_AGENTS.md](docs/MCP_FOR_AGENTS.md)** (catalog, workflows, system-prompt snippet).

**Installed tool:**

```json
{
  "mcpServers": {
    "dotnetmap": {
      "command": "dotnetmap",
      "args": ["serve-mcp", "--db", "C:/path/to/repo/.dotnetmap/index.db"]
    }
  }
}
```

**From source:**

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

| Kind | Names |
|------|--------|
| **Tools** | `get_status`, `doctor`, `get_overview`, `search`, `get_type`, `get_method`, `get_snippet`, `get_callers`, `get_consumers`, `find_implementations`, `find_overrides`, `get_impact`, `get_hotspots` |
| **Resources** | `solution://overview`, `type://{name}`, `method://{name}` |
| **Prompts** | `architecture_review`, `impact_analysis`, `refactor_plan` |

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
dotnet tool install -g DotNetMap.Tool --add-source ./artifacts --version 1.0.0
dotnetmap index ./MySolution --db .dotnetmap/index.db
```

See [docs/RELEASE.md](docs/RELEASE.md) for install notes, exit codes, and multi-TFM/Linux tips.

## License

MIT — see [LICENSE](LICENSE).
