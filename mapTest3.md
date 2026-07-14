# DotNetMap: DemoSolution

- Path: `C:\Users\Cesar\source\repos\DotNetMap\samples\DemoSolution\DemoSolution.slnx`
- Indexed (UTC): 2026-07-14 11:49:57Z
- Mode: `structure+light-deps`
- Projects: 2 | Types: 4 | Members: 12 | Files: 3
- Token estimate (overview): ~310

## Types

- **Demo.App.OrderService** (class, public, 2 members) — Default implementation.
  - deps: `[{"kind":"implements","targetId":"type:Demo.Core.IOrderService","targetName":"Demo.Core.IOrderService"}]`
  - `public decimal CalculateTotal(IReadOnlyList<OrderLine> lines)`
  - `public Task<Guid> SaveAsync(Order order, CancellationToken cancellationToken = default(CancellationToken))`
- **Demo.Core.IOrderService** (interface, public, 2 members) — Contract for order pricing and persistence.
  - `decimal CalculateTotal(IReadOnlyList<OrderLine> lines)` — Calculates the total for the given order lines.
  - `Task<Guid> SaveAsync(Order order, CancellationToken cancellationToken = default(CancellationToken))` — Persists the order and returns its id.
- **Demo.Core.Order** (class, public, 4 members) — Customer order aggregate root.
  - `public decimal GetSubtotal()` — Sum of line totals.
  - `public required string CustomerName`
  - `public Guid Id`
  - `public List<OrderLine> Lines`
- **Demo.Core.OrderLine** (class, public, 4 members) — Single line item on an order.
  - `public decimal LineTotal`
  - `public int Quantity`
  - `public required string Sku`
  - `public decimal UnitPrice`

_~373 tokens_
