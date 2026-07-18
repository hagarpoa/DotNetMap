namespace Demo.Core;

// DNM-025 — language surface scenarios (see samples/DemoSolution/README.md)

/// <summary>Generic service interface.</summary>
public interface IRepository<T> where T : class
{
    T? Find(string id);
}

/// <summary>Record DTO.</summary>
public sealed record Money(decimal Amount, string Currency);

/// <summary>Value type for coordinates.</summary>
public readonly struct Point2D(double X, double Y)
{
    public double Magnitude => Math.Sqrt(X * X + Y * Y);
}

/// <summary>Order lifecycle states.</summary>
public enum OrderStatus
{
    Draft = 0,
    Submitted = 1,
    Fulfilled = 2,
    Cancelled = 3
}

/// <summary>Generic repository sample.</summary>
public sealed class InMemoryRepository<T> : IRepository<T> where T : class
{
    private readonly Dictionary<string, T> _items = new(StringComparer.Ordinal);

    public event EventHandler<string>? ItemAdded;

    public T? Find(string id) => _items.TryGetValue(id, out var v) ? v : null;

    public void Add(string id, T item)
    {
        _items[id] = item;
        ItemAdded?.Invoke(this, id);
    }

    public T this[string id] => _items[id];
}

/// <summary>Nested type host.</summary>
public sealed class NestedHost
{
    /// <summary>Nested public class.</summary>
    public sealed class NestedItem
    {
        public required string Name { get; init; }
    }

    public NestedItem Create(string name) => new() { Name = name };
}

/// <summary>Attribute for sample decoration.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DemoTagAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Class with attribute + async API.</summary>
[DemoTag("surface")]
public sealed class TaggedAsyncService
{
    public async Task<int> CountAsync(IEnumerable<int> values, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return values.Count();
    }
}
