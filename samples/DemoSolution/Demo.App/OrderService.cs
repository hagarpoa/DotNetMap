using Demo.Core;

namespace Demo.App;

/// <summary>
/// Default <see cref="IOrderService"/> implementation.
/// </summary>
public sealed class OrderService : IOrderService
{
    /// <inheritdoc />
    public decimal CalculateTotal(IReadOnlyList<OrderLine> lines) =>
        lines.Sum(l => l.LineTotal);

    /// <inheritdoc />
    public Task<Guid> SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Demo call edge for DotNetMap method graph (SaveAsync → CalculateTotal).
        _ = CalculateTotal(order.Lines);
        // TODO: replace with real persistence (sample marker for query --body / DNM-013).
        // Demo only — no real persistence.
        return Task.FromResult(order.Id);
    }

    public Task<Guid> SaveAsync2(Order order, CancellationToken cancellationToken = default)
    {
        // Demo call edge for DotNetMap method graph (SaveAsync → CalculateTotal).
        _ = CalculateTotal(order.Lines);
        // Demo only — no real persistence.
        return Task.FromResult(order.Id);
    }

}
