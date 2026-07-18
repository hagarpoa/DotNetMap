# Changelog

All notable changes to **DotNetMap** are documented here.

## [1.0.0] — 2026-07-18

### Added

- Sample language surface (generics, records, structs, enums, nested types, events, attributes, multi-TFM) — DNM-025  
- Multi-TFM project sample + TFM on `status --verbose` project list — DNM-018  
- Classic `DemoSolution.sln` alongside `.slnx` — DNM-038  
- MCP resource `method://{name}` — DNM-039  
- `docs/TROUBLESHOOTING.md`, `SECURITY.md`, release checklist — DNM-024/030/040  
- Linux/macOS smoke notes in troubleshooting — DNM-029  

### Included from 0.3.x

- MCP impact cycle (callers, consumers, snippets, impact, hotspots, doctor)  
- Body FTS (`--index-body`), edges table (schema v1), partials `locations[]`  
- Generated code policy (`--include-generated`)  
- Config file `.dotnetmap.json`  

### Install

```powershell
dotnet pack src/DotNetMap.Cli/DotNetMap.Cli.csproj -c Release -o artifacts
dotnet tool install -g DotNetMap.Tool --add-source artifacts --version 1.0.0
```

## [0.3.5] — 2026-07-18

MSBuild hardening messages, snippet allowlist + MCP hard caps, RELEASE docs.

## [0.3.4] — 2026-07-18

DNM-017 generated code policy.

## [0.3.3] — 2026-07-18

DNM-014 edges + DNM-016 partials.

## [0.3.2] — 2026-07-18

DNM-013 body FTS.

## [0.3.1] — 2026-07-18

DNM-026/027 config + golden export.

## [0.3.0] — 2026-07-14

AI agent cycle (MCP tools, staleness, quality, playbook).

## [0.2.0]

Core CLI + MCP map, incremental, callers.
