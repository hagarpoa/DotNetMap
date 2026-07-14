# DotNetMap — Backlog de melhorias

**Estado base:** v0.2.0  
**Objetivo do produto:** índice local, AI-token-aware, de solutions .NET para agentes (refatoração, impacto, navegação).  
**Última revisão:** 2026-07-14  

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

### DNM-013 — Full-text no body (FTS opcional)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Store · Extração |
| **Esforço** | L |
| **Depende** | — |

**Problema:** achar strings, magic numbers, TODOs no código.  
**Solução:** FTS5 opcional de conteúdo (ou path+line index); flag `--index-body` (pesado). Default off.  
**Done when:** `query --body "TODO"` encontra ocorrências com file/line.

---

### DNM-014 — Tabela normalizada de relações (schema v1)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Store |
| **Esforço** | L |
| **Depende** | — |

**Problema:** JSON de relações limita queries e grafos.  
**Solução:** tabelas `edges(from_id, to_id, kind, file, line)`; migração `schema_version=1`; manter JSON como cache de export se útil.  
**Done when:** query SQL de grafo 2 hops; reindex migra ou rebuild documentado.

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

### DNM-016 — Partials: merge completo de declarações
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | M |
| **Depende** | — |

**Problema:** type/member em múltiplos arquivos; span “primário” só.  
**Solução:** listar todas as partial locations; members por arquivo.  
**Done when:** type partial com 2 files aparece com `locations[]`.

---

### DNM-017 — Source generators / generated code policy
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | M |
| **Depende** | — |

**Solução:** detectar `IsGenerated` / AnalyzerConfig; flag `--include-generated`; marcar `isGenerated` no nó.  
**Done when:** generated default excluído; flag inclui com label.

---

### DNM-018 — Multi-TFM / multi-target projects
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | L |
| **Depende** | — |

**Problema:** MSBuildWorkspace + multi-TFM é frágil.  
**Solução:** escolher TFM preferido; documentar; testes com sample multi-target.  
**Done when:** project multi-TFM indexa sem crash; TFM no `status`/project node.

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

### DNM-024 — Hardening MSBuildWorkspace (CI/máquinas reais)
| | |
|--|--|
| **Pri** | P1 (para 1.0) |
| **Área** | Extração · DX |
| **Esforço** | L |
| **Depende** | — |

**Solução:** mensagens claras para SDK missing, restore needed, global.json; log de WorkspaceFailed filtrado.  
**Done when:** doc de troubleshooting; exit codes estáveis.

---

### DNM-025 — Sample “feio” (partials, generics, records, multi-project)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Qualidade |
| **Esforço** | M |
| **Depende** | — |

**Done when:** integração cobre ≥8 cenários de linguagem listados no sample README.

---

### DNM-026 — Testes de regressão de snapshot de export
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Qualidade |
| **Esforço** | S–M |
| **Depende** | — |

**Done when:** export MD do DemoSolution comparado a golden file (normalizado paths).

---

### DNM-027 — Config file `.dotnetmap.yml` / `dotnetmap.json`
| | |
|--|--|
| **Pri** | P2 |
| **Área** | CLI |
| **Esforço** | M |
| **Depende** | — |

**Solução:** excludes, includePrivate, maxCalls, bcl filter, db path default.  
**Done when:** index sem flags usa config do diretório.

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

### DNM-029 — Linux/macOS smoke
| | |
|--|--|
| **Pri** | P2 |
| **Área** | DX |
| **Esforço** | M |
| **Depende** | DNM-024 |

**Done when:** CI (ou doc validado) indexa sample no Linux com SDK.

---

### DNM-030 — Segurança: path allowlist e max results em MCP
| | |
|--|--|
| **Pri** | P1 (para 1.0) |
| **Área** | MCP |
| **Esforço** | S–M |
| **Depende** | DNM-004 |

**Solução:** snippet só lê paths dentro da solution root; caps obrigatórios.  
**Done when:** tentativa de path traversal falha com erro claro.

---

## P1 — Release 1.0 (produto)

### DNM-031 — Publicação NuGet / versionamento semântico
| | |
|--|--|
| **Pri** | P1 |
| **Área** | DX |
| **Esforço** | M |
| **Depende** | — |

**Done when:** `dotnet tool install -g DotNetMap.Tool` do nuget.org (ou feed privado documentado); changelog.

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

### DNM-035 — Schema versioning policy + migrations
| | |
|--|--|
| **Pri** | P1 |
| **Área** | Store |
| **Esforço** | M |
| **Depende** | DNM-014 se v1 |

**Done when:** abrir db antigo com tool nova falha com “reindex required” ou migra.

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

### DNM-038 — Suporte oficial `.slnx` e solutions legadas (matriz)
| | |
|--|--|
| **Pri** | P2 |
| **Área** | Extração |
| **Esforço** | M |
| **Depende** | — |

**Done when:** testes com `.sln` e `.slnx`.

---

### DNM-039 — Resource MCP `method://{id}`
| | |
|--|--|
| **Pri** | P2 |
| **Área** | MCP |
| **Esforço** | S |
| **Depende** | — |

**Done when:** resource retorna mesmo conteúdo que `get_method`.

---

### DNM-040 — Checklist de release 1.0
| | |
|--|--|
| **Pri** | P1 |
| **Área** | DX |
| **Esforço** | S |
| **Depende** | DNM-031, DNM-024, DNM-030 |

**Done when:** SECURITY.md, CHANGELOG, playbook, sample, pack, version, CI green.

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

| Necessidade do modelo | Status 0.2 | Backlog |
|----------------------|------------|---------|
| Overview solution | OK | — |
| Buscar por nome | OK | DNM-013 (body) |
| Definição type/method + lines | OK | DNM-004 (snippet), DNM-016 (partials) |
| Calls outbound | OK | DNM-007 (filtro BCL) |
| Callers method | CLI only | DNM-001, DNM-005 |
| Consumers type | CLI only | DNM-002 |
| Implementations | Parcial | DNM-003 |
| Overrides | Não | DNM-011 |
| Field/prop refs | Parcial | DNM-012 |
| Impact multi-hop | Não | DNM-015 |
| Testes relacionados | Não | DNM-019 |
| Dead code | Não | DNM-020 |
| Índice stale | Fraco | DNM-009 |
| Body search | Não | DNM-013 |
| Monorepo robusto | Parcial | DNM-018, DNM-024, DNM-029 |

---

## Sprint 0.3 sugerido (ordem de PRs)

1. **DNM-006 + DNM-007** ✅ — saída compacta + filtro BCL  
2. **DNM-001 + DNM-002** ✅ — MCP callers/consumers  
3. **DNM-004** ✅ — snippets  
4. **DNM-005** ✅ — call sites com linha  
5. **DNM-008 + DNM-010** ✅ — prompts + playbook  
6. **DNM-009** ✅ — status stale  

**Sprint 0.3: completo.**

---

## Critérios de “pronto para 1.0” (produto)

- [ ] Agente MCP faz impact analysis **sem** CLI extra (DNM-001/002/004)  
- [ ] Respostas default cabem em ~12k chars por tool (DNM-006)  
- [ ] `doctor` + troubleshooting MSBuild (DNM-034/024)  
- [ ] Tool instalável com versão limpa (já) e feed documentado (DNM-031)  
- [ ] Sample + testes cobrem language surface principal (DNM-025/026)  
- [ ] Snippets seguros (DNM-030)  
- [ ] Playbook de refatoração publicado (DNM-010)  

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
