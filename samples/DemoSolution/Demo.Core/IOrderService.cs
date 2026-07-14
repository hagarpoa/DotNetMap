namespace Demo.Core;

/// <summary>
/// Contract for order pricing and persistence.
/// </summary>
public interface IOrderService
{
    /// <summary>Calculates the total for the given order lines.</summary>
    decimal CalculateTotal(IReadOnlyList<OrderLine> lines);

    /// <summary>Persists the order and returns its id.</summary>
    Task<Guid> SaveAsync(Order order, CancellationToken cancellationToken = default);
}
