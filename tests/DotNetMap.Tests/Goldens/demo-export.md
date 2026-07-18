# DotNetMap: DemoSolution

- Path: `$SOLUTION/DemoSolution.slnx`
- Indexed (UTC): $TIMESTAMP
- Mode: `structure+light-deps` | Detail: `compact`
- Projects: 2 | Types: 6 | Members: 15 | Files: 5
- Token estimate (overview): ~TOKENS

## Types

- **Demo.App.OrderService** (class, public, 3 members) — Default implementation.
  - Structural (1): `Demo.Core.IOrderService`
- `public decimal CalculateTotal(IReadOnlyList<OrderLine> lines)`  L11-12 (2 lines)
- `public Task<Guid> SaveAsync(Order order, CancellationToken cancellationToken = default(CancellationToken))`  L15-21 (7 lines)
  - calls (1): `Demo.App.OrderService.CalculateTotal`
- `public Task<Guid> SaveAsync2(Order order, CancellationToken cancellationToken = default(CancellationToken))`  L23-29 (7 lines)
  - calls (1): `Demo.App.OrderService.CalculateTotal`
- **Demo.App.VipOrderCalculator** (class, public, 1 members) — VIP pricing: 10% discount via override.
  - Structural (1): `Demo.Core.OrderCalculator`
- `public override decimal Adjust(decimal total)`  L9-9 (1 lines)
- **Demo.Core.IOrderService** (interface, public, 2 members) — Contract for order pricing and persistence.
- `decimal CalculateTotal(IReadOnlyList<OrderLine> lines)`  L9-9 (1 lines)
- `Task<Guid> SaveAsync(Order order, CancellationToken cancellationToken = default(CancellationToken))`  L12-12 (1 lines)
- **Demo.Core.Order** (class, public, 4 members) — Customer order aggregate root.
- `public decimal GetSubtotal()`  L17-17 (1 lines)
- `public required string CustomerName`  L10-10 (1 lines)
- `public Guid Id`  L8-8 (1 lines)
- `public List<OrderLine> Lines`  L12-12 (1 lines)
- **Demo.Core.OrderCalculator** (class, public, 1 members) — Base calculator with overridable adjustment.
- `public virtual decimal Adjust(decimal total)`  L7-7 (1 lines)
- **Demo.Core.OrderLine** (class, public, 4 members) — Single line item on an order.
- `public decimal LineTotal`  L26-26 (1 lines)
- `public int Quantity`  L24-24 (1 lines)
- `public required string Sku`  L23-23 (1 lines)
- `public decimal UnitPrice`  L25-25 (1 lines)

_~534 tokens_
