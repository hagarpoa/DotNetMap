# Documentation index — DotNetMap 1.0.0

## Phase 1 (product) completeness

| Artifact | Path | Status |
|----------|------|--------|
| README product entry | [README.md](../README.md) | ✅ |
| MCP guide for AI context | [MCP_FOR_AGENTS.md](MCP_FOR_AGENTS.md) | ✅ |
| Agent refactor playbooks | [AGENT_PLAYBOOK.md](AGENT_PLAYBOOK.md) | ✅ |
| Release / install / exit codes | [RELEASE.md](RELEASE.md) | ✅ |
| MSBuild & path troubleshooting | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | ✅ |
| Technical decisions | [DECISIONS.md](DECISIONS.md) | ✅ |
| Product backlog | [BACKLOG.md](BACKLOG.md) | ✅ (1.0 criteria checked) |
| Historical MVP PR checklist | [MVP_CHECKLIST.md](MVP_CHECKLIST.md) | ✅ (historical) |
| Security policy | [SECURITY.md](../SECURITY.md) | ✅ |
| Changelog | [CHANGELOG.md](../CHANGELOG.md) | ✅ |
| License | [LICENSE](../LICENSE) | ✅ |
| Sample scenarios | [samples/DemoSolution/README.md](../samples/DemoSolution/README.md) | ✅ |
| Tool package | `artifacts/DotNetMap.Tool.1.0.0.nupkg` | ✅ local pack |
| nuget.org publish | — | ⏳ maintainer API key |

## Recommended reading order

1. **Humans / setup:** README → RELEASE → TROUBLESHOOTING  
2. **AI host config:** MCP_FOR_AGENTS (system prompt) → AGENT_PLAYBOOK (workflows)  
3. **Contributors:** DECISIONS → BACKLOG  

## Gaps (explicit, non-blocking for local 1.0)

- Publishing `DotNetMap.Tool` to nuget.org (documented; needs API key).  
- Optional CI Linux job (smoke commands documented in TROUBLESHOOTING).  
- Post-1.0 features: related tests, dead-code report, file-level incremental (BACKLOG).
