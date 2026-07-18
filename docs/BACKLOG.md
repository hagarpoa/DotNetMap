# DotNetMap — Backlog de melhorias

**Estado base:** v0.3.4  
**Objetivo do produto:** índice local, AI-token-aware, de solutions .NET para agentes (refatoração, impacto, navegação).  
**Última revisão:** 2026-07-18 (1.0 hardening: DNM-024/030/031 docs)  

---

## Como usar este backlog

| Campo | Significado |
|-------|-------------|
| **ID** | Identificador estável (`DNM-xxx`) |
| **Pri** | P0 bloqueia valor AI · P1 alto · P2 médio · P3 nice-to-have |
| **Área** | MCP · CLI · Extração · Store · Tokens · DX · Qualidade |
| **Esforço** | T-shirt: S ≤0.5d · M 1–2d · L 3–5d · XL >5d |
| **Depende** | IDs pré-requisito |

**Ordem de releases sugerida**

| Release | Foco | Itens |
|---------|------|--------|
| **0.3** | Fechar ciclo AI no MCP + snippets | DNM-001…010 |
| **0.4** | Impacto fino (sites, implementations) | DNM-011…020 |
| **0.5** | Qualidade de índice + monorepo | DNM-021…030 |
| **1.0** | Empacotamento, hardening, docs produto | DNM-031…040 |
| **Depois** | Embeddings, métricas avançadas, multi-lang | DNM-041… |

---

## Já entregue (v0.2.0) — não reabrir sem motivo

- [x] Extração estrutural Roslyn (solution/project/ns/type/member)
- [x] Spans (file, lines, offsets, size, lineCount)
- [x] Light deps (inherits, implements, signature/member types)
- [x] Method **calls** outbound no index
- [x] Consumers de **type** on-demand (`consumers` / `--relations`)
- [x] Callers de **method** on-demand (CLI `callers`)
- [x] FTS5 search, export MD/JSON, get type/method
- [x] Incremental project-level (`--changed-only`)
- [x] MCP: `get_status`, `get_overview`, `search`, `get_type`, `get_method`
- [x] Resources + prompts básicos
- [x] `dotnet tool` pack, versão limpa sem `+githash`

---

## P0 / P1 — Release 0.3 (ciclo AI fechado)

### DNM-001 — MCP: tool `get_callers` ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P0 |
| **Área** | MCP |
| **Esforço** | S–M |
| **Depende** | — (CLI `callers` já existe) |

- [x] `ImpactAnalysis.GetCallersAsync` compartilhado CLI/MCP
- [x] Tool `get_callers(name, max, updateDb, format, detail)`
- [x] CLI `callers` usa a mesma camada

**Done when:** MCP lista tool; chamada retorna callers compactos. ✅

---

### DNM-002 — MCP: tool `get_consumers` (type) ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P0 |
| **Área** | MCP |
| **Esforço** | S–M |
| **Depende** | — |

- [x] `ImpactAnalysis.GetTypeConsumersAsync`
- [x] Tool `get_consumers(name, max, updateDb, format, detail)`
- [x] CLI `consumers` multi-scope permanece para batch

**Done when:** `IOrderService` no sample retorna `OrderService` via API. ✅

---

### DNM-003 — MCP: tool `find_implementations` ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | MCP · Extração |
| **Esforço** | M |
| **Depende** | DNM-002 (pode compartilhar código) |

- [x] `HierarchyQueries.FindImplementationsAsync` (SymbolFinder)
- [x] CLI `implementations <type>`
- [x] MCP `find_implementations`
- [x] Sites com file:line

**Done when:** interface do sample lista implementors; output com file/lines. ✅

---

### DNM-004 — Snippet de fonte on-demand ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P0 |
| **Área** | CLI · MCP · Store |
| **Esforço** | M |
| **Depende** | — |

- [x] `SourceSnippetReader` (disk, maxChars, path under solution root)
- [x] CLI `get --snippet [--context N]` e `snippet <name>`
- [x] MCP `get_snippet` + `includeSnippet` em `get_type`/`get_method`
- [x] Não grava source no SQLite

**Done when:** `get_method` com `includeSnippet=true` devolve body truncado com segurança. ✅

---

### DNM-005 — Call sites com arquivo + linha ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | Extração · Store · MCP |
| **Esforço** | M–L |
| **Depende** | DNM-001 |

- [x] `RelationRef.File` / `Line` / `SiteLabel`
- [x] `MethodCallersExtractor` emite **um entry por site**
- [x] MD: `` `Caller` @ `file:Lline` ``; JSON `sites[]`
- [x] Persistência em `consumers_json` via `--update` / `updateDb`

**Done when:** `callers CalculateTotal` lista site em `OrderService.cs:L…`. ✅

---

### DNM-006 — Limites de token e shape de resposta unificados ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | Tokens · MCP · CLI |
| **Esforço** | S–M |
| **Depende** | — |

- [x] `--detail compact|full` (CLI + MCP)
- [x] Separar `calls` / structural / signatureDeps / consumers na saída
- [x] Contagens + `truncated` + `maxChars` (12k default)
- [x] Caps documentados (`OutputLimits`)

**Done when:** `get_method` compacto mostra só calls (sem Task/CancellationToken por default). ✅

---

### DNM-007 — Filtro de calls BCL / external ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | Extração · Tokens |
| **Esforço** | S |
| **Depende** | DNM-006 |

- [x] Default: só calls solution-local
- [x] `--include-external-calls` / `--include-external-signature-deps`
- [x] Signature BCL filtrada por default no index

**Done when:** `SaveAsync` lista `CalculateTotal` sem ruído BCL. ✅

---

### DNM-008 — Prompt MCP: `refactor_plan` ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | MCP |
| **Esforço** | S |
| **Depende** | DNM-001, DNM-002 |

- [x] Prompt `refactor_plan` com workflow completo
- [x] Prompts `architecture_review` / `impact_analysis` atualizados (stale, snippet, callers)

**Done when:** prompt documentado e listável no MCP. ✅

---

### DNM-009 — Status: staleness do índice ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | CLI · Store |
| **Esforço** | M |
| **Depende** | — |

- [x] `IndexStaleness.Check` (hashes em disco vs index)
- [x] `status` imprime Stale YES/no + projetos
- [x] `status --check` → exit code **4** se stale
- [x] MCP `get_status` → `isStale`, `staleProjects`, …

**Done when:** após editar um `.cs` sem reindex, `status` mostra stale ≥1. ✅

---

### DNM-010 — Documentação: playbook de refatoração para agentes ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | DX |
| **Esforço** | S |
| **Depende** | — |

- [x] `docs/AGENT_PLAYBOOK.md` (rename, extract, split, interface)
- [x] Link no README

**Done when:** README linka playbook; cobre rename method, extract method, split class. ✅

---

## P1 / P2 — Release 0.4 (impacto fino)

### DNM-011 — `find_overrides` / cadeia virtual ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração · MCP |
| **Esforço** | M |
| **Depende** | — |

- [x] `HierarchyQueries.FindOverridesAsync`
- [x] CLI `overrides <method>`
- [x] MCP `find_overrides`
- [x] Sample `OrderCalculator` / `VipOrderCalculator.Adjust`

**Done when:** sample com hierarquia virtual cobre o caso. ✅

---

### DNM-012 — Consumers/callers de **property** e **field** ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração · CLI · MCP |
| **Esforço** | M |
| **Depende** | DNM-001, DNM-005 |

- [x] `MemberReferencesExtractor` (method/property/field/event)
- [x] `get_callers` / CLI `callers` resolvem qualquer member kind
- [x] Sites com file:line
- [x] Teste `OrderLine.LineTotal`

**Done when:** property do sample tem callers on-demand. ✅

---

### DNM-013 — Full-text no body (FTS opcional) ✅ (0.3.2)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Store · Extração |
| **Esforço** | L |
| **Depende** | — |

- [x] `body_fts` (schema v0) + meta `index_body` / `body_file_count`
- [x] `index --index-body` / config `indexBody` (default off; max 512k chars/file)
- [x] `query --body` + MCP `search(body=true)` com file:line + snippet
- [x] Sample `TODO` em OrderService; testes `BodyFtsTests`

**Done when:** `query --body "TODO"` encontra ocorrências com file/line. ✅

---

### DNM-014 — Tabela normalizada de relações (schema v1) ✅ (0.3.3)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Store |
| **Esforço** | L |
| **Depende** | — |

- [x] Tabela `edges(from_id, to_id, kind, file, line)` + índices
- [x] `WriteMap` materializa edges; JSON permanece como cache de export
- [x] `schema_version=1`; migração automática JSON→edges ao abrir DB v0
- [x] `GetOutboundEdges` / `GetInboundEdges` / `QueryGraphHops` (2 hops)
- [x] `ImpactGraph` hops profundos usam tabela edges quando disponível
- [x] Testes `EdgesTests`

**Done when:** query SQL de grafo 2 hops; reindex migra ou rebuild documentado. ✅

---

### DNM-015 — Grafo de impacto multi-hop compacto ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | MCP · Store |
| **Esforço** | M–L |
| **Depende** | DNM-001, DNM-002, idealmente DNM-014 |

- [x] `ImpactGraph.BuildAsync` (BFS, depth, maxNodes, direction)
- [x] Hop 0 live (callers/consumers/implementations); hops seguintes via índice
- [x] CLI `impact` + MCP `get_impact`
- [x] MD/JSON compactos com caps + truncated

**Done when:** output tree/list com token estimate e caps. ✅

---

### DNM-016 — Partials: merge completo de declarações ✅ (0.3.3)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | M |
| **Depende** | — |

- [x] `DeclarationLocation` + `TypeNode.Locations` / `locations_json`
- [x] `StructureExtractor` acumula sites de cada partial; members por arquivo
- [x] `get` / export MD+JSON com `locations[]` (partials)
- [x] Sample: `Order` + `Order.Pricing.cs`; testes `PartialsTests`

**Done when:** type partial com 2 files aparece com `locations[]`. ✅

---

### DNM-017 — Source generators / generated code policy ✅ (0.3.4)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | M |
| **Depende** | — |

- [x] Heurística de path (`*.g.cs`, designer, `/Generated/`) + atributos `GeneratedCode` / `CompilerGenerated`
- [x] Default exclui; `--include-generated` / config `includeGenerated`
- [x] `isGenerated` em file/type/member + label no export
- [x] Sample `OrderGenerated.g.cs`; testes `GeneratedCodeTests`

**Done when:** generated default excluído; flag inclui com label. ✅

---

### DNM-018 — Multi-TFM / multi-target projects ✅ (1.0.0)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | L |
| **Depende** | — |

- [x] TFM lido do csproj (primeiro de `TargetFrameworks` se multi)
- [x] Sample `Demo.MultiTfm` (net8.0;net10.0)
- [x] `status --verbose` lista `tfm=` por projeto
- [x] Doc em RELEASE/TROUBLESHOOTING

**Done when:** project multi-TFM indexa sem crash; TFM no `status`/project node. ✅

---

### DNM-019 — Link testes ↔ produção (heurística)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração · MCP |
| **Esforço** | L |
| **Depende** | — |

**Solução:** naming (`FooTests` → `Foo`), refs de projeto de teste; tool `related_tests(type)`.  
**Done when:** sample com projeto de teste lista related tests.

---

### DNM-020 — Dead code report (batch callers)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | CLI |
| **Esforço** | L |
| **Depende** | DNM-001 |

**Solução:** `dotnetmap dead-code [--project X] [--public-only]` com sample rate / limites (nunca monorepo inteiro sem aviso).  
**Done when:** report lista candidates com 0 callers (com disclaimer de reflection).

---

## P2 — Release 0.5 (qualidade e monorepo)

### DNM-021 — Incremental por arquivo (com recompile de project)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | L |
| **Depende** | — |

**Nota:** invalidação continua project-level para correção semântica; otimizar só I/O de write se útil.  
**Done when:** documentado se/ quando file-level write path é seguro.

---

### DNM-022 — Métricas de qualidade do índice ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Store · CLI |
| **Esforço** | M |
| **Depende** | — |

- [x] `IndexQuality.Compute` (summaries, calls %, avg lines, consumers, grade A–D)
- [x] `status --verbose`
- [x] `get_status` inclui `quality`

**Done when:** status imprime quality block. ✅

---

### DNM-023 — Hotspots (métodos grandes / high fan-in) ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | CLI · MCP |
| **Esforço** | M |
| **Depende** | DNM-001 opcional |

- [x] `Hotspots.Compute` — size | calls | fanin | types
- [x] CLI `hotspots --by … --top N`
- [x] MCP `get_hotspots`
- [x] Index-only (rápido); fanin usa consumers_json se populado

**Done when:** sample retorna SaveAsync etc. por size/calls. ✅

---

### DNM-024 — Hardening MSBuildWorkspace (CI/máquinas reais) ✅ (0.3.5)
| | |
|--|--|
| **Pri** | P1 (para 1.0) |
| **Área** | Extração · DX |
| **Esforço** | L |
| **Depende** | — |

- [x] `WorkspaceLoader.FormatOpenFailure` (SDK / restore / global.json hints)
- [x] Filtro de diagnostics ruidosos (ex. MSB3277)
- [x] `doctor` detalha path MSBuild + global.json
- [x] `docs/TROUBLESHOOTING.md`

**Done when:** doc de troubleshooting; exit codes estáveis. ✅ (doc + msgs; exit codes já parciais)

---

### DNM-025 — Sample “feio” (partials, generics, records, multi-project) ✅ (1.0.0)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Qualidade |
| **Esforço** | M |
| **Depende** | — |

- [x] `LanguageSurface.cs` + partials/generated/multi-tfm
- [x] `samples/DemoSolution/README.md` com ≥8 cenários
- [x] Testes `LanguageSurfaceAndTfmTests`

**Done when:** integração cobre ≥8 cenários de linguagem listados no sample README. ✅

---

### DNM-026 — Testes de regressão de snapshot de export ✅ (0.3.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Qualidade |
| **Esforço** | S–M |
| **Depende** | — |

- [x] `ExportSnapshotTests` — export MD do DemoSolution com paths/timestamps normalizados
- [x] Golden `tests/DotNetMap.Tests/Goldens/demo-export.md`
- [x] Refresh com `DOTNETMAP_UPDATE_GOLDEN=1`

**Done when:** export MD do DemoSolution comparado a golden file (normalizado paths). ✅

---

### DNM-027 — Config file `.dotnetmap.yml` / `dotnetmap.json` ✅ (0.3.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | CLI |
| **Esforço** | M |
| **Depende** | — |

- [x] `DotNetMapConfig` — `.dotnetmap.json` / `dotnetmap.json` (discover walk-up)
- [x] Campos: `db`, `includePrivate`, `includeTest`, external calls/sig deps, `relations`, `excludeProjects`, `maxCalls`
- [x] CLI flags explícitos vencem config; `--config`, `--exclude-project`, `--max-calls`
- [x] `index` / `status` / `export` / `doctor` / `serve-mcp` resolvem `db` da config

**Done when:** index sem flags usa config do diretório. ✅

---

### DNM-028 — Progresso e cancelamento amigáveis
| | |
|--|--|
| **Pri** | P3 |
| **Área** | CLI |
| **Esforço** | S |
| **Depende** | — |

**Done when:** index em solution média mostra %/projeto e respeita Ctrl+C.

---

### DNM-029 — Linux/macOS smoke ✅ (1.0.0 doc)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | DX |
| **Esforço** | M |
| **Depende** | DNM-024 |

- [x] Smoke commands em TROUBLESHOOTING + RELEASE
- [ ] CI Linux opcional (futuro)

**Done when:** CI (ou doc validado) indexa sample no Linux com SDK. ✅ doc

---

### DNM-030 — Segurança: path allowlist e max results em MCP ✅ (0.3.5)
| | |
|--|--|
| **Pri** | P1 (para 1.0) |
| **Área** | MCP |
| **Esforço** | S–M |
| **Depende** | DNM-004 |

- [x] Snippet allowlist solution root; rejeita `..` / absolute sem root
- [x] `OutputLimits` hard caps; MCP tools clamp max/search/snippet/impact
- [x] Testes de path traversal

**Done when:** tentativa de path traversal falha com erro claro. ✅

---

## P1 — Release 1.0 (produto)

### DNM-031 — Publicação NuGet / versionamento semântico ✅ parcial (0.3.5)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | DX |
| **Esforço** | M |
| **Depende** | — |

- [x] Pack local documentado (`RELEASE.md`); version 0.3.4 alinhada
- [x] Checklist de publish nuget.org no RELEASE
- [ ] Pacote publicado em nuget.org (requer API key do maintainer)

**Done when:** `dotnet tool install -g DotNetMap.Tool` do nuget.org (ou feed privado documentado); changelog. (local pack ✅)

---

### DNM-032 — Rename pasta do repositório / branding final
| | |
|--|--|
| **Pri** | P3 |
| **Área** | DX |
| **Esforço** | S |
| **Depende** | — |

**Done when:** repo folder `DotNetMap` alinhado ao product name (opcional).

---

### DNM-033 — Telemetria opt-in (local only stats)
| | |
|--|--|
| **Pri** | P3 |
| **Área** | DX |
| **Esforço** | M |
| **Depende** | — |

**Solução:** contadores locais de tempo de index (sem rede). Default off.

---

### DNM-034 — Comando `doctor` ✅ (0.2.1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | CLI |
| **Esforço** | M |
| **Depende** | DNM-024 |

- [x] Checks: runtime, MSBuild, DB, schema, index_data, solution_path, staleness, quality, writable
- [x] CLI `doctor` + MCP `doctor`
- [x] Exit 1 se checks severity=error falharem

**Done when:** `dotnetmap doctor` imprime checklist pass/fail. ✅

---

### DNM-035 — Schema versioning policy + migrations ✅ parcial (0.3.3)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | Store |
| **Esforço** | M |
| **Depende** | DNM-014 se v1 |

- [x] `schema_version=1` no write; migração v0→edges (JSON backfill)
- [ ] Política formal de breaking changes / mensagem “reindex required” para v2+

**Done when:** abrir db antigo com tool nova falha com “reindex required” ou migra. (migra ✅)

---

### DNM-036 — Exit codes e JSON machine-readable em todos os comandos
| | |
|--|--|
| **Pri** | P2 |
| **Área** | CLI |
| **Esforço** | S–M |
| **Depende** | — |

**Done when:** `--format json` em status/callers/consumers; tabela de exit codes no README (já parcial).

---

### DNM-037 — Performance budget documentado
| | |
|--|--|
| **Pri** | P2 |
| **Área** | DX · Qualidade |
| **Esforço** | M |
| **Depende** | — |

**Done when:** doc com tempos esperados (sample, 50 projetos structure-only).

---

### DNM-038 — Suporte oficial `.slnx` e solutions legadas (matriz) ✅ (1.0.0)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | M |
| **Depende** | — |

- [x] `DemoSolution.sln` + `.slnx`
- [x] Teste index via classic `.sln`

**Done when:** testes com `.sln` e `.slnx`. ✅

---

### DNM-039 — Resource MCP `method://{id}` ✅ (1.0.0)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | MCP |
| **Esforço** | S |
| **Depende** | — |

- [x] `method://{name}` → mesmo Markdown que `get_method`

**Done when:** resource retorna mesmo conteúdo que `get_method`. ✅

---

### DNM-040 — Checklist de release 1.0 ✅ (1.0.0)
| | |
|--|--|
| **Pri** | P1 |
| **Área** | DX |
| **Esforço** | S |
| **Depende** | DNM-031, DNM-024, DNM-030 |

- [x] SECURITY.md, CHANGELOG, playbook, sample, pack, version 1.0.0
- [ ] nuget.org push (API key do maintainer)

**Done when:** SECURITY.md, CHANGELOG, playbook, sample, pack, version, CI green. ✅ (pack local; CI opcional)

---

## Depois de 1.0 (exploratório)

### DNM-041 — Embeddings / sqlite-vec (busca híbrida)
| **Pri** | P3 | **Esforço** | XL |

### DNM-042 — Export TOON (token-optimized notation)
| **Pri** | P3 | **Esforço** | M |

### DNM-043 — Materializar call graph completo (opt-in nightly)
| **Pri** | P3 | **Esforço** | XL |

### DNM-044 — UI web local (read-only graph)
| **Pri** | P3 | **Esforço** | XL |

### DNM-045 — Multi-linguagem (não-.NET)
| **Pri** | P3 | **Esforço** | XL | **Nota:** fora do posicionamento core |

### DNM-046 — Integração nativa com Graphite/IDE extension
| **Pri** | P3 | **Esforço** | XL |

### DNM-047 — Clone/duplication detection
| **Pri** | P3 | **Esforço** | L–XL |

### DNM-048 — Diff de API pública entre dois índices
| **Pri** | P3 | **Esforço** | L |

### DNM-049 — Complexidade ciclomática / maintainability hotspots
| **Pri** | P3 | **Esforço** | M |

### DNM-050 — Cache de Compilation entre runs (avançado)
| **Pri** | P3 | **Esforço** | XL |

---

## Mapa: necessidade de refatoração → itens

| Necessidade do modelo | Status 0.3.5 | Backlog restante |
|----------------------|--------------|------------------|
| Overview solution | OK | — |
| Buscar por nome | OK | — |
| Definição type/method + lines + partials | OK | — |
| Calls outbound + filtro BCL | OK | — |
| Callers / consumers / implementations / overrides | OK (MCP+CLI) | — |
| Impact multi-hop + edges SQL | OK | — |
| Body search | OK (`--index-body`) | — |
| Índice stale / doctor / quality | OK | — |
| Generated code policy | OK | — |
| Testes relacionados | Não | DNM-019 |
| Dead code | Não | DNM-020 |
| Multi-TFM / monorepo | Parcial | DNM-018, DNM-021, DNM-029 |
| NuGet.org | Pack local OK | DNM-031 publish |

---

## Sprint 0.3 sugerido (ordem de PRs)

**Sprint 0.3–0.3.4: completo** (incl. body FTS, edges, partials, generated).

---

## Critérios de “pronto para 1.0” (produto)

- [x] Agente MCP faz impact analysis **sem** CLI extra (DNM-001/002/004/015)  
- [x] Respostas default cabem em ~12k chars por tool (DNM-006)  
- [x] `doctor` + troubleshooting MSBuild (DNM-034/024)  
- [x] Tool instalável via **pack local** documentado (nuget.org opcional — DNM-031)  
- [x] Sample + testes language surface (DNM-025/026)  
- [x] Snippets seguros + caps MCP (DNM-030)  
- [x] Playbook de refatoração publicado (DNM-010)  
- [x] SECURITY + CHANGELOG + version 1.0.0 (DNM-040)  

---

## Fora de escopo consciente (não colocar no 1.0)

- Substituir IDE / Roslyn Code Actions  
- Garantir rename 100% (reflection, dynamic, source gen avançado)  
- Servidor multi-tenant / cloud index  
- Análise de runtime / profiling  

---

## Manutenção deste documento

- Ao fechar item: marcar `[x]` e mover para seção “Entregue” com versão.  
- Novas ideias: criar `DNM-0xx` no fim da prioridade adequada.  
- Não implementar P3 se P0/P1 da release atual estiverem abertos.
