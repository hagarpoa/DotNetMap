# NetMap — Checklist MVP (issues / PRs)

Ordem recomendada de entrega. Cada item = PR focada, mergeável isoladamente.

Estimativa total: **13–16 dias** (buffer realista). Target original 10–13 só com happy path.

---

## PR-0 — Setup & contratos (0.5 d) ✅

- [x] Solution `NetMap.slnx` (.NET 10)
- [x] Projetos `NetMap.Core`, `NetMap.Cli`, `NetMap.Tests`
- [x] Sample `samples/DemoSolution` (Order / IOrderService / OrderService)
- [x] Docs: decisões, schema, este checklist
- [x] `Directory.Build.props` (nullable, version)
- [x] `.gitignore` + README de uso local

**Done when:** `dotnet build` e `dotnet test` passam. ✅

---

## PR-1 — Domain + SQLite schema + Store (1.5 d) ✅ (esqueleto)

- [x] Modelos de domínio (`SolutionMap`, `ProjectNode`, `TypeNode`, `MemberNode`, `SourceSpan`, `RelationRef`)
- [x] Aplicar `schema/v0.sql` na abertura do DB (embedded resource)
- [x] `MapStore`: Create/Open, WriteMap (transação), ReadMeta/Status, counts
- [x] `schema_version` = 0
- [x] Testes: create DB, insert minimal map, reopen, assert counts
- [x] CLI `index` (stub), `status`, `export` (overview compacto)

**Done when:** teste de integração grava/lê um mapa mínimo sem Roslyn. ✅

**Próximo:** PR-3 — query FTS + export completo de membros.

---

## PR-2 — WorkspaceLoader + extração estrutural (2.5–3 d) ✅

- [x] Carregar `.sln` / `.slnx` / `.csproj` via MSBuildWorkspace + MSBuildLocator
- [x] Extrair projects, namespaces, types, properties, methods, fields, events
- [x] `SourceSpan` (file, lines, offsets) + size
- [x] XML `<summary>` quando existir
- [x] Filtros: bin/obj, migrations, generated, testes (default off), private (default off)
- [x] IDs estáveis `type:`, `method:`, `property:`
- [x] Light deps básicas (inherits / implements / signature types) — adiantado da PR-4
- [x] Teste de integração no `DemoSolution`
- [x] Export MD/JSON com lista de types

**Done when:** `netmap index samples/DemoSolution` gera DB com types/methods esperados. ✅

---

## PR-3 — CLI index / status / export / query (1.5 d) ✅

- [x] `index` com flags documentadas
- [x] `status` (última indexação, counts, mode, db size)
- [x] `export --format md|json` compacto + `TokenEstimate` + `--members`
- [x] `query` via FTS5 (nome + summary) + `--kind` + fallback LIKE
- [x] `get <type>` — detalhe de um type com members (AI-friendly)
- [x] Códigos de saída: 0 ok, 1 erro, 2 empty db, 3 not found / no matches

**Done when:** fluxo index → status → export → query → get funciona no sample. ✅

---

## PR-4 — Light deps + relations opcionais (2 d) ✅

- [x] Light deps: inherits, implements, uses_in_signature, uses_in_member
- [x] Persistência em `DependenciesJson` / `ConsumersJson`
- [x] `--relations type:Name|project:Name|full` no `index`
- [x] Comando `consumers` on-demand sobre índice existente
- [x] `--full-relations` = consumers para todos os types (com warning de custo)
- [x] Implementors de interfaces + SymbolFinder
- [x] Export/get mostram deps e consumers

**Done when:** consumers on-demand + scoped relations. ✅

---

## PR-5 — MCP Server (1.5–2 d) ✅

- [x] Subcomando `serve-mcp` (stdio, ModelContextProtocol 1.4)
- [x] Tools: `get_overview`, `get_type`, `search`, `get_status`
- [x] Default compacto + limites (max types/hits/members)
- [x] Resources: `solution://overview`, `type://{name}`
- [x] Prompts: `architecture_review`, `impact_analysis`
- [x] Descrições de tools com exemplos

**Done when:** `netmap serve-mcp` sobe e tools leem o SQLite. ✅

---

## PR-6 — Incremental + packaging + docs (1.5–2 d) ✅

- [x] Hash SHA-256 por arquivo; fingerprint por projeto
- [x] `--changed-only` (project-level) + `--force`
- [x] Pack como `dotnet tool` (`NetMap.Tool` → comando `netmap`)
- [x] README + `docs/RELEASE.md`
- [x] Smoke: sample full cycle + second pass reuses projects

**Done when:** segunda indexação de solution intacta reutiliza projetos; tool packável. ✅

---

## Fora do MVP (backlog consciente)

| Item | Quando |
|------|--------|
| TOON export | se houver consumer real |
| Tabelas normalizadas de relações | schema v1 |
| Embeddings / sqlite-vec | Fase 2 |
| Snippet on-demand da fonte | pós-MCP |
| Métricas de qualidade do índice | Fase 2 |
| Consumers on-demand por símbolo (MCP) | logo após PR-5 se PR-4 full for pesado |

---

## Critérios de aceite do produto v1.0

1. Indexa solution de até ~50 projetos em minutos (structure + light deps), não horas.
2. Agente obtém overview + type + method sem estourar ~12k chars por tool call.
3. Status da base é confiável (quando indexou, o que tem, mode).
4. Reindex incremental por projeto funciona no caminho feliz.
5. Documentação deixa claro: **sem grafo completo de referências por default**.
