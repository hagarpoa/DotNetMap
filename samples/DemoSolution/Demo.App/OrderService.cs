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
        // Demo only — no real persistence.
        return Task.FromResult(order.Id);
    }
}
