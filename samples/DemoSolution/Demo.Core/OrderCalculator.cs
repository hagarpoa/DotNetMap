namespace Demo.Core;

/// <summary>Base calculator with overridable adjustment.</summary>
public abstract class OrderCalculator
{
    /// <summary>Adjust a subtotal (override in derived calculators).</summary>
    public virtual decimal Adjust(decimal total) => total;
}
