# DotNetMap — Decisões técnicas (MVP)

## Posicionamento

> **DotNetMap** = índice local, auditável e *AI-token-aware* de uma solution .NET, com posição/tamanho/sumário e consulta MCP, **sem** exigir grafo completo de referências.

Não é call-graph universal. Não é IDE. É mapa de contexto para agentes.

---

## Stack (confirmada)

| Peça | Escolha | Motivo |
|------|---------|--------|
| Runtime | .NET 10 | LTS atual do projeto, `.slnx` maduro |
| Análise | Roslyn 5.x + MSBuildWorkspace | Oficial, símbolos reais |
| Persistência | SQLite + FTS5 | Local-first, zero servidor, busca textual |
| CLI | System.CommandLine | Padrão Microsoft 2026 |
| MCP | Model Context Protocol SDK (Fase 5) | Tools/Resources/Prompts oficiais |
| Empacotamento | `dotnet tool` único `dotnetmap` | Um binário, vários subcomandos |

---

## Extração em camadas

### Fase A — default (`structure` + `light-deps`)

Inclui:

- Solution → Project → Namespace → Type (class/record/struct/interface/enum)
- Property / Method / Event / Field (configurável)
- Posição no fonte (`file`, `start_line`, `end_line`, `start_offset`, `end_offset`)
- Tamanho em caracteres (span do símbolo)
- Sumário XML (`/// <summary>`) quando existir; senão `null`
- **Light deps** (baratas, sem `SymbolFinder.FindReferences`):
  1. Base type
  2. Interfaces implementadas
  3. Tipos em assinaturas (params, return, type args, attributes)
  4. Tipos em campos/propriedades

**Não** inclui usings como dependência semântica (ruído alto). Usings podem existir só como metadado de arquivo no futuro.

### Fase B — sob demanda

- Consumers / referências via `SymbolFinder` **scoped**:
  - por projeto: `--relations project:Nome`
  - por tipo: MCP `get_consumers(symbol)`
  - global `--full-relations` só com aviso de custo
- **Não** materializar grafo completo por default no SQLite

---

## Visibilidade e filtros default

| Default | Comportamento |
|---------|----------------|
| Visibilidade | `public` + `internal` |
| Private | só com `--include-private` |
| Testes | excluir `**/*Test*`, `**/Tests/**`, projetos com `IsTestProject` se detectável |
| bin/obj | sempre excluídos |
| Migrations | excluídos por default (`**/Migrations/**`) |

---

## Incremental (MVP)

- Hash SHA-256 do conteúdo do arquivo
- Se **qualquer** arquivo de um projeto mudar → **reindexar o projeto inteiro**
- Não reindexar solution completa se só um projeto mudou
- Semântica documentada: “project-level invalidation”, não “method-level”

---

## Relações no schema

MVP: colunas JSON (`DependenciesJson`, opcionalmente `ConsumersJson` quando Phase B scoped rodou).

Contrato JSON (array de objetos):

```json
[
  {
    "kind": "inherits|implements|uses_in_signature|uses_in_member",
    "targetId": "type:Demo.Core.IOrderService",
    "targetName": "Demo.Core.IOrderService"
  }
]
```

IDs estáveis: `{kind}:{fullyQualifiedMetadataName}`  
Exemplos: `type:Demo.Core.Order`, `method:Demo.Core.Order.CalculateTotal(System.Decimal)`

`schema_version` na tabela `meta`. Reindex total aceitável na v1→v2.

---

## Saída e tokens

- Default CLI/MCP: **compacto** (árvore + assinaturas + sumário curto)
- Expansão: `--include-source-snippet`, `--include-full-relations`, `detail=full`
- Campo `TokenEstimate` calculado (heurística ~ chars/4) em exports e tools
- Formatos MVP: Markdown compacto + JSON minificado
- TOON: opcional pós-MVP (não bloqueia v1)

Limites default de tools MCP:

- `max_results`: 50
- `max_chars`: 12_000
- `max_depth` (grafo): 2

---

## O que fica de fora do MVP

- Embeddings / sqlite-vec
- Call graph completo materializado
- Tabelas normalizadas de relações
- Armazenar source completo no DB (só posição + hash; snippet on demand)
- UI web
- Suporte multi-linguagem

---

## Packaging CLI

```text
dotnetmap index <path> [--db .dotnetmap/index.db] [--full-relations] [--include-private] [--include-test]
dotnetmap status [--db ...]
dotnetmap query <text> [--db ...]
dotnetmap export [--format md|json] [--out map.md]
dotnetmap serve-mcp [--db ...]   # Fase 5
```
