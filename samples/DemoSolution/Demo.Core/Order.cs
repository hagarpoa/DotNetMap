namespace Demo.Core;

/// <summary>
/// Customer order aggregate root.
/// </summary>
public sealed partial class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string CustomerName { get; init; }

    public List<OrderLine> Lines { get; } = [];
}

/// <summary>Single line item on an order.</summary>
public sealed class OrderLine
{
    public required string Sku { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal => Quantity * UnitPrice;
}
