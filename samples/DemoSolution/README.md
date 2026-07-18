# DemoSolution

Small multi-project sample for DotNetMap integration tests and docs.

## Projects

| Project | Role |
|---------|------|
| **Demo.Core** | Domain types, partials, language surface, generated sample |
| **Demo.App** | Services implementing Core contracts |
| **Demo.MultiTfm** | Multi-target `net8.0;net10.0` (DNM-018) |

## Language scenarios (≥8) — DNM-025

| # | Scenario | Where |
|---|----------|--------|
| 1 | Interface + implementor | `IOrderService` / `OrderService` |
| 2 | Class hierarchy + override | `OrderCalculator` / `VipOrderCalculator` |
| 3 | Partial type (2 files) | `Order` + `Order.Pricing.cs` |
| 4 | Property + computed + callers | `OrderLine.LineTotal` |
| 5 | Method calls (outbound) | `OrderService.SaveAsync` → `CalculateTotal` |
| 6 | Generic type + constraint | `IRepository<T>`, `InMemoryRepository<T>` |
| 7 | `record` | `Money` |
| 8 | `struct` | `Point2D` |
| 9 | `enum` | `OrderStatus` |
| 10 | Nested type | `NestedHost.NestedItem` |
| 11 | Event | `InMemoryRepository.ItemAdded` |
| 12 | Attribute | `DemoTagAttribute` / `TaggedAsyncService` |
| 13 | Async method | `TaggedAsyncService.CountAsync` |
| 14 | Indexer | `InMemoryRepository.this[string]` |
| 15 | Generated source (`*.g.cs`) | `OrderGenerated.g.cs` (needs `--include-generated`) |
| 16 | Multi-TFM | `Demo.MultiTfm` |

## Solutions

- `DemoSolution.slnx` — modern solution (default)
- `DemoSolution.sln` — classic format (DNM-038 matrix)

## Index smoke

```powershell
dotnetmap index samples/DemoSolution --db .dotnetmap/demo.db
dotnetmap status --db .dotnetmap/demo.db --verbose
dotnetmap get Money --db .dotnetmap/demo.db
dotnetmap get SharedApi --db .dotnetmap/demo.db
```
