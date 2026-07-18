namespace Demo.Core;

/// <summary>
/// Pricing helpers for <see cref="Order"/> (second partial file — DNM-016 sample).
/// </summary>
public sealed partial class Order
{
    /// <summary>
    /// Sum of line totals.
    /// </summary>
    public decimal GetSubtotal() => Lines.Sum(l => l.LineTotal);
}
