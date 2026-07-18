# DotNetMap — Agent playbook (refactoring)

How an AI agent should use DotNetMap to refactor .NET code with low tokens and accurate impact.

**Requires:** indexed DB (`.dotnetmap/index.db`) and MCP `serve-mcp` or CLI.

> **Full MCP catalog for AI context:** [MCP_FOR_AGENTS.md](MCP_FOR_AGENTS.md)  
> Paste that file into the agent’s system/context when configuring DotNetMap MCP.

---

## Golden rules

1. **Always `get_status` first.** If `isStale` is true, reindex before trusting the map:
   ```text
   dotnetmap index <path> --changed-only --db .dotnetmap/index.db
   ```
2. Prefer **compact** outputs (`detail=compact`). Use `full` / `includeSnippet` only when editing.
3. Do **not** invent callers/consumers — only tool results.
4. Use **file:Lline** from tools for edits; use `get_snippet` instead of reading whole files.
5. After edits: reindex `--changed-only`, then re-run impact tools.

---

## Tool map

| Goal | Tool / CLI |
|------|------------|
| Index health | `get_status` / `status` / `status --check` / `status --verbose` / `doctor` |
| Browse solution | `get_overview` / `export` |
| Find symbol | `search` / `query` |
| Type API + lines | `get_type` / `get Type` |
| Method + calls | `get_method` / `get Type.Method` |
| Source body | `get_snippet` / `get --snippet` / `snippet` |
| Who references method/property/field | `get_callers` / `callers` (sites with `file:Lline`) |
| Who uses type | `get_consumers` / `consumers type:Name` |
| Implementors / subclasses | `find_implementations` / `implementations` |
| Method overrides | `find_overrides` / `overrides` |
| Multi-hop impact graph | `get_impact` / `impact` |
| What to refactor first | `get_hotspots` / `hotspots --by size|calls` |
| Plan refactor | prompt `refactor_plan` |

---

## Playbook A — Rename / change method signature

```text
1. get_status
2. search(query="OldName", kind="member")
3. get_method(name="Type.OldName")
4. get_callers(name="Type.OldName")     # each site: file + line
5. get_snippet on the method + critical call sites
6. Edit declaration + every site from step 4
7. index --changed-only
8. get_callers again to confirm
```

**Risk:** reflection / dynamic / incomplete index if stale.

---

## Playbook B — Extract method

```text
1. get_status
2. get_method(name="Type.BigMethod", includeSnippet=true)
3. Note Lstart–Lend and outbound calls
4. Design new method; get_type for placement
5. Edit source (extract)
6. index --changed-only
7. get_method on old + new; get_callers if public API
```

---

## Playbook C — Split class / move type

```text
1. get_status
2. get_type(name="FatType")
3. get_consumers(name="FatType")
4. get_method on public members that move; get_callers for each if needed
5. Plan new type + namespace/project
6. Edit + update namespaces/usings (agent/IDE)
7. index --changed-only
8. get_consumers on old/new names
```

---

## Playbook D — Interface / implementation change

```text
1. get_status
2. get_type(name="IService")
3. find_implementations(name="IService")   # hierarchy only
4. get_consumers(name="IService")          # all refs if needed
5. get_method on interface members; find_overrides / get_callers as needed
6. Edit interface + each implementor
7. index --changed-only
```

---

## Token budget tips

| Do | Don't |
|----|--------|
| `detail=compact` | Dump full `dependenciesJson` strings |
| `get_snippet` for one method | Open entire project files |
| Cap `max` on search/callers | `get_callers` on every method blindly |
| Reuse overview once | Re-export whole map every turn |

**Defaults:** ~12k chars/tool, 20 relations listed, solution-local calls only (no BCL noise).

---

## Exit codes (CLI)

| Code | Meaning |
|------|---------|
| 0 | OK |
| 1 | Error |
| 2 | Empty DB |
| 3 | Not found / no matches |
| 4 | `status --check` and index is **stale** |

---

## MCP config sketch

```json
{
  "mcpServers": {
    "dotnetmap": {
      "command": "dotnet",
      "args": [
        "run", "--project", "src/DotNetMap.Cli", "--",
        "serve-mcp", "--db", ".dotnetmap/index.db"
      ]
    }
  }
}
```

Prompts: `architecture_review`, `impact_analysis`, `refactor_plan`.

---

## When DotNetMap is not enough

- Runtime behavior / reflection  
- Build diagnostics (use `dotnet build`)  
- Test discovery (no test↔prod graph yet — backlog DNM-019)  
- Full-text inside method bodies without snippet (backlog DNM-013)  
