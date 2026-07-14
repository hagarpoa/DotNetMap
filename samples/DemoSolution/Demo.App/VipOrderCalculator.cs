using Demo.Core;

namespace Demo.App;

/// <summary>VIP pricing: 10% discount via override.</summary>
public sealed class VipOrderCalculator : OrderCalculator
{
    /// <inheritdoc />
    public override decimal Adjust(decimal total) => total * 0.9m;
}
