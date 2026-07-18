# DotNetMap 1.0.0 — Release notes

## What it is

Local-first, AI-token-aware semantic map of a .NET solution:

- **CLI** — index, query, get, impact, hotspots, doctor, export, serve-mcp  
- **MCP** — stdio tools + resources (`solution://`, `type://`, `method://`)  
- **SQLite** — FTS5, optional body FTS, normalized `edges`  

## Install (local pack)

```powershell
cd C:\Projects\AI\NetMap
dotnet test
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet tool uninstall -g DotNetMap.Tool   # if reinstalling
dotnet tool install -g DotNetMap.Tool --add-source artifacts --version 1.0.0
dotnetmap --version
```

## Install from nuget.org

When published:

```powershell
dotnet tool install -g DotNetMap.Tool
dotnet tool update -g DotNetMap.Tool
```

### Maintainer publish

```powershell
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet nuget push artifacts/DotNetMap.Tool.1.0.0.nupkg --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json
git tag v1.0.0
git push --tags
```

## Exit codes (CLI)

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (exception, doctor fail, body FTS missing, etc.) |
| 2 | Empty database |
| 3 | Not found / no query matches |
| 4 | `status --check` and index is stale |

## 1.0 checklist (DNM-040)

- [x] SECURITY.md  
- [x] CHANGELOG.md  
- [x] AGENT_PLAYBOOK.md  
- [x] TROUBLESHOOTING.md  
- [x] Sample surface + README (DNM-025)  
- [x] Pack as `DotNetMap.Tool` version clean (no +githash)  
- [x] Version 1.0.0  
- [x] Tests green  
- [x] MSBuild troubleshooting (DNM-024)  
- [x] Snippet path allowlist + MCP caps (DNM-030)  
- [ ] nuget.org publish (maintainer API key)  

## Multi-TFM note (DNM-018)

MSBuildWorkspace loads **one** TFM configuration per project. Multi-target projects store the first listed TFM as `name (of a;b)` in project metadata. Prefer single-TFM for large monorepos when possible.

## Linux / macOS (DNM-029)

```bash
dotnet --list-sdks
dotnet restore samples/DemoSolution
dotnet run --project src/DotNetMap.Cli -- index samples/DemoSolution --db /tmp/dotnetmap-demo.db
dotnet run --project src/DotNetMap.Cli -- status --db /tmp/dotnetmap-demo.db
dotnet test
```

Requires .NET 10 SDK (and net8.0 pack if building `Demo.MultiTfm`).

## Compatibility

- .NET 10 SDK  
- Windows tested; Linux/macOS via SDK MSBuildLocator  
