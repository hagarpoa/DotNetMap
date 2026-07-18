# DotNetMap — Troubleshooting

## MSBuild / workspace failures (DNM-024)

DotNetMap uses **Roslyn MSBuildWorkspace** via **MSBuildLocator**. Index needs a real MSBuild from the .NET SDK or Visual Studio Build Tools.

### Quick checks

```powershell
dotnet --info
dotnet --list-sdks
dotnet restore path\to\solution.slnx
dotnetmap doctor --db .dotnetmap/index.db
```

### Common errors

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `Could not register MSBuild` / no instances | SDK or VS Build Tools missing | Install [.NET SDK](https://dotnet.microsoft.com/download) and/or [Build Tools](https://visualstudio.microsoft.com/downloads/) |
| `Failed to open workspace` + restore / `project.assets.json` | Packages not restored | `dotnet restore <solution>` then re-index |
| `global.json` / SDK version mismatch | Pinned SDK not installed | Install pinned SDK or relax `global.json` |
| Empty projects / 0 types after index | Design-time load failed silently | Check stderr for `workspace:` lines; restore; open solution in VS once |
| Works on one machine, not CI | No MSBuild in image | Use SDK image with matching TFMs; run `dotnet restore` in CI |
| Multi-TFM project flaky | MSBuildWorkspace multi-target limits | Prefer single TFM for indexing, or open specific `.csproj` (DNM-018 planned) |

### Filtered diagnostics

Some MSBuild design-time warnings (e.g. binding redirects **MSB3277**) are filtered as noise. Remaining `workspace:` lines during `index` usually matter.

### Linux / macOS

Install the .NET SDK matching the solution TFM. MSBuildLocator uses the SDK MSBuild. See DNM-029 for smoke validation.

---

## Snippet / path security (DNM-030)

`get_snippet` / `get --snippet` only read files **under the indexed solution root**.

- Relative paths with `..` that escape the root → error  
- Absolute paths outside the root → error  
- MCP tools clamp `max` / `maxChars` (hard caps in `OutputLimits`)

---

## Database / schema

```text
schema_version=1  → edges table (DNM-014)
Older DBs        → auto-migrate JSON → edges on open
```

If tools fail after upgrade, re-index:

```powershell
dotnetmap index path\to\solution --db .dotnetmap/index.db --force
```

---

## Stale index

```powershell
dotnetmap status --db .dotnetmap/index.db --check   # exit 4 if stale
dotnetmap index path\to\solution --db .dotnetmap/index.db --changed-only
```
