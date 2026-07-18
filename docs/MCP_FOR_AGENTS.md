# DotNetMap MCP — AI agent context

**Version:** 1.0.0  
**Purpose:** Local, token-efficient semantic map of a .NET solution for coding agents.  
**Transport:** MCP over **stdio** (no network).

Use this document as system/context when the agent has DotNetMap MCP tools available.

---

## 1. What DotNetMap is (and is not)

| Is | Is not |
|----|--------|
| Structure index: Solution → Project → Namespace → Type → Member | Full IDE / Roslyn code-fix engine |
| Spans (`file`, `startLine`–`endLine`), XML summaries, light deps | Guaranteed 100% rename (reflection/dynamic) |
| On-demand callers/consumers via SymbolFinder | Default full call graph for the monorepo |
| Compact MD/JSON for small token budgets | Cloud multi-tenant index |

**Default index:** public+internal members, no tests, no generated files, solution-local method calls only, light deps (inherits/implements/signature types + outbound calls).

---

## 2. Prerequisites (human or agent shell)

1. Index once (or when stale):

```powershell
dotnetmap index <path-to.sln|.slnx|dir> --db .dotnetmap/index.db
# optional: --changed-only after small edits
# optional: --index-body for search(body=true) / query --body
```

2. Start MCP (examples):

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

**From repo (dev):**

```json
{
  "mcpServers": {
    "dotnetmap": {
      "command": "dotnet",
      "args": [
        "run", "--project", "src/DotNetMap.Cli", "--no-build",
        "--", "serve-mcp", "--db", ".dotnetmap/index.db"
      ],
      "cwd": "C:/path/to/DotNetMap"
    }
  }
}
```

The DB path is fixed for the server process; tools always open that database.

---

## 3. Golden rules (always)

1. **Call `get_status` first.** If `isStale` is true, tell the user to reindex (`dotnetmap index <path> --changed-only --db …`) before trusting results.
2. Prefer **`detail=compact`** (default). Use `detail=full` / `includeSnippet=true` only when editing.
3. **Never invent** callers, consumers, implementors, or file:line sites — only tool output.
4. Prefer **`get_snippet`** over reading whole source files.
5. After edits: reindex `--changed-only`, then re-run impact tools.
6. Respect caps: large `max` values are clamped (see §7).

---

## 4. Tools (complete catalog)

All tools return **Markdown** by default unless `format=json`. Errors are usually plain text strings.

### 4.1 Health

| Tool | Parameters | Returns | When to use |
|------|------------|---------|-------------|
| **`get_status`** | _(none)_ | JSON: solution, counts, `isStale`, `staleProjects`, `quality`, flags (`includePrivate`, `indexBody`, `edgeCount`, …) | **Always first** |
| **`doctor`** | _(none)_ | Markdown checklist (runtime, MSBuild, DB, schema, stale, quality) | Setup / “why index fails” |

### 4.2 Navigation & search

| Tool | Parameters | Returns | When to use |
|------|------------|---------|-------------|
| **`get_overview`** | `maxTypes` (def 80), `detail` compact\|full | Compact type list | Orientation |
| **`search`** | `query`, `kind` all\|type\|member, `max` (def 15), `format`, `detail`, **`body`** bool | Hits list; body hits as `file:Lline` | Find symbols; `body=true` needs `--index-body` |
| **`get_type`** | `name`, `format`, `maxMembers`, `detail`, `includeSnippet`, `contextLines` | Type API, lines, structural deps, members | Inspect a type |
| **`get_method`** | `name`, `format`, `detail`, `includeSnippet`, `contextLines` | Method lines, outbound **calls** | Inspect a method |
| **`get_snippet`** | `name`, `contextLines` 0–20, `maxChars`, `format` | Numbered source lines from **disk** | Read only the span you need |

**Name resolution:** short name, `Full.Name`, `Type.Member`, or ids like `type:…` / `method:…`.

### 4.3 Impact (often live SymbolFinder — can be slower)

| Tool | Parameters | Returns | When to use |
|------|------------|---------|-------------|
| **`get_callers`** | `name`, `max`, **`updateDb`**, `format`, `detail` | Reference **sites** with `file:Lline` (method/property/field) | Rename / signature change |
| **`get_consumers`** | `name`, `max`, **`updateDb`**, `format`, `detail` | Types that reference the type | Type move / API surface |
| **`find_implementations`** | `name`, `max`, `format` | Implementors / subclasses | Interface / base class |
| **`find_overrides`** | `name`, `max`, `format` | Override / interface-impl methods | Virtual / abstract |
| **`get_impact`** | `name`, `depth` 1–4, `maxNodes`, `direction` both\|in\|out, `noLive`, `format` | Multi-hop graph | Broad impact planning |
| **`get_hotspots`** | `by` size\|calls\|fanin\|types, `top`, `format` | Ranked symbols | What to refactor first |

**Notes:**

- `updateDb=true` persists sites into SQLite (`consumers_json`) for faster fan-in/hotspots later.
- `get_impact`: hop 0 can use live SymbolFinder; deeper hops use the **edges** table (schema v1).
- `noLive=true` → index-only walk (faster, may miss unmaterialized refs).

---

## 5. Resources (URI templates)

| URI | Content |
|-----|---------|
| `solution://overview` | Same idea as `get_overview` (Markdown) |
| `type://{name}` | Same as `get_type` Markdown for `name` |
| `method://{name}` | Same as `get_method` Markdown for `name` |

Use resources when the host prefers resource reads; tools are richer (params, JSON, snippets).

---

## 6. Prompts (templates)

| Prompt | Args | Use |
|--------|------|-----|
| **`architecture_review`** | optional `focus` | Layering / coupling review |
| **`impact_analysis`** | required `target` | Change-impact report |
| **`refactor_plan`** | required `goal` | Rename / extract / split plan |

Prompts encode the same golden rules (status → search → impact → snippet → plan).

---

## 7. Token & safety limits

| Limit | Typical default | Hard ceiling |
|-------|-----------------|--------------|
| Response chars | ~12 000 | 24 000 |
| Search hits | 15 | 100 |
| Members per type | 80 | 200 |
| Callers/consumers | 50 | 100 |
| Snippet chars | 4 000 | 8 000 |
| Impact nodes | 40 | 80 |
| Relations shown | 20 | — |

**Security:** snippets only read under the **solution root** of the index. Path traversal is rejected.

**Output shapes:**

- `detail=compact` — calls + structural deps; fewer signature BCL noise  
- `detail=full` — more signature deps / fields  

---

## 8. Recommended workflows

### A. Session start

```text
get_status
→ if isStale: stop, ask for reindex
→ else: get_overview or search
```

### B. Rename method / change signature

```text
get_status
search(query="OldName", kind="member")
get_method(name="Type.OldName")
get_callers(name="Type.OldName")     # every site: file:Lline
get_snippet on method + critical sites
→ edit declaration + all sites
→ user: index --changed-only
get_callers again to verify
```

### C. Change interface / type API

```text
get_status
get_type(name="IService")
find_implementations(name="IService")
get_consumers(name="IService")
get_method on members as needed
→ edit + implementors
→ reindex --changed-only
```

### D. Broad impact / “what breaks?”

```text
get_status
get_impact(name="Symbol", depth=2, direction="both")
get_hotspots(by="size") or by="calls"
```

### E. Find TODO / string in source

```text
# requires index built with --index-body
search(query="TODO", body=true)
```

---

## 9. CLI ↔ MCP cheat sheet

| Task | MCP | CLI |
|------|-----|-----|
| Health | `get_status` / `doctor` | `status` / `doctor` |
| Search | `search` | `query` |
| Type | `get_type` | `get Type` |
| Method | `get_method` | `get Type.Method` |
| Snippet | `get_snippet` | `snippet` / `get --snippet` |
| Callers | `get_callers` | `callers` |
| Consumers | `get_consumers` | `consumers type:Name` |
| Implementations | `find_implementations` | `implementations` |
| Overrides | `find_overrides` | `overrides` |
| Impact | `get_impact` | `impact` |
| Hotspots | `get_hotspots` | `hotspots` |
| Reindex | _(shell)_ | `index … --changed-only` |

MCP does **not** reindex by itself; the agent or user must run the CLI (or a host terminal tool).

---

## 10. Index flags agents should know

| Flag / config | Effect |
|---------------|--------|
| `--changed-only` | Project-level incremental reindex |
| `--include-private` | Private/protected members |
| `--include-test` | Test projects |
| `--include-generated` | `*.g.cs` / designer / GeneratedCode |
| `--index-body` | Enables `search(body=true)` |
| `--include-external-calls` | BCL/NuGet calls in index |
| `.dotnetmap.json` | Defaults for db, flags, excludes |

---

## 11. Failure modes & honesty

| Symptom | Meaning | Action |
|---------|---------|--------|
| DB not found / empty | No index | `dotnetmap index …` |
| `isStale: true` | Files changed on disk | `--changed-only` reindex |
| Empty callers | No refs found **or** incomplete analysis | Don’t invent sites; try `updateDb`, full name, or note reflection |
| `body FTS not indexed` | Index without `--index-body` | Reindex with flag |
| MSBuild errors | SDK/restore/global.json | `doctor` + TROUBLESHOOTING.md |

**Never claim** “zero callers” as proof of dead code without disclaimers (reflection, dynamic, incomplete index).

---

## 12. Minimal system prompt snippet

Copy into agent system instructions:

```text
You have DotNetMap MCP tools for a .NET solution map (local SQLite index).

Rules:
1. Call get_status first. If isStale, request reindex before trusting the map.
2. Prefer detail=compact. Use get_snippet instead of reading whole files.
3. Never invent callers/consumers/implementations — only tool results.
4. Cite file:Lline from tools when proposing edits.
5. After code changes, recommend: dotnetmap index <path> --changed-only --db <db>
6. For renames: get_method + get_callers; for types: get_type + get_consumers / find_implementations.
7. Use get_impact for multi-hop planning; get_hotspots to prioritize refactors.

Catalog: get_status, doctor, get_overview, search, get_type, get_method, get_snippet,
get_callers, get_consumers, find_implementations, find_overrides, get_impact, get_hotspots.
Resources: solution://overview, type://{name}, method://{name}.
Prompts: architecture_review, impact_analysis, refactor_plan.
```

---

## 13. Related docs

| Doc | Role |
|-----|------|
| [AGENT_PLAYBOOK.md](AGENT_PLAYBOOK.md) | Longer refactor playbooks |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | MSBuild / restore / paths |
| [RELEASE.md](RELEASE.md) | Install, exit codes, 1.0 notes |
| [SECURITY.md](../SECURITY.md) | Threat model, allowlist |
| [DECISIONS.md](DECISIONS.md) | Architecture decisions |
| [BACKLOG.md](BACKLOG.md) | Post-1.0 ideas |

---

*End of MCP agent context — DotNetMap 1.0.0*
