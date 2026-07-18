# DotNetMap 0.3.4 — Release notes

## What it is

Local-first, AI-token-aware semantic map of a .NET solution:

- **CLI** for index / query / export / impact / hotspots / doctor  
- **MCP** stdio server for agents  
- **SQLite** persistence with FTS5, optional body FTS, normalized `edges`  

## Versioning

Semantic versioning on the **tool package** `DotNetMap.Tool`:

| Version | Highlights |
|---------|------------|
| 0.3.4 | DNM-017 generated code policy |
| 0.3.3 | DNM-014 edges + DNM-016 partials |
| 0.3.2 | DNM-013 body FTS |
| 0.3.1 | DNM-026/027 config + golden export |
| 0.3.0 | AI cycle: MCP impact tools, snippets, staleness… |
| 0.2.0 | Core CLI/MCP map |

## Install (local pack — always works)

```powershell
cd C:\Projects\AI\NetMap
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet tool uninstall -g DotNetMap.Tool   # if reinstalling
dotnet tool install -g DotNetMap.Tool --add-source artifacts --version 0.3.4
dotnetmap --version
dotnetmap --help
```

Or run without installing:

```powershell
dotnet run --project src/DotNetMap.Cli -- --help
```

## Install from nuget.org (DNM-031)

When the package is published to nuget.org:

```powershell
dotnet tool install -g DotNetMap.Tool
# or update:
dotnet tool update -g DotNetMap.Tool
```

### Maintainer publish checklist

1. Bump `Version` / `PackageVersion` in `src/DotNetMap.Cli/DotNetMap.Cli.csproj` and `Directory.Build.props`.  
2. `dotnet test` green.  
3. `dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts`  
4. `dotnet nuget push artifacts/DotNetMap.Tool.<version>.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json`  
5. Tag git: `v0.3.4`  

Until step 4 is done, document **local pack** (above) as the supported install path.

## Quickstart

```powershell
dotnetmap index path/to/solution.slnx --db .dotnetmap/index.db
dotnetmap status --db .dotnetmap/index.db
dotnetmap doctor --db .dotnetmap/index.db
dotnetmap query OrderService --db .dotnetmap/index.db
dotnetmap get OrderService --db .dotnetmap/index.db
dotnetmap impact IOrderService --db .dotnetmap/index.db
dotnetmap serve-mcp --db .dotnetmap/index.db
```

Optional:

```powershell
dotnetmap index path --index-body --include-generated --changed-only
dotnetmap query TODO --body
```

## Troubleshooting

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for MSBuild, restore, global.json, and snippet path security.

## Planned work

[BACKLOG.md](BACKLOG.md) — remaining path to 1.0: multi-TFM, NuGet.org publish, sample surface, Linux smoke.

## Compatibility

- .NET 10 SDK  
- Windows tested; MSBuildLocator uses installed VS/SDK MSBuild  
