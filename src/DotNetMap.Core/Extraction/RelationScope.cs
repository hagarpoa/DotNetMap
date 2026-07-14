namespace DotNetMap.Core.Extraction;

public enum RelationScopeKind
{
    Type,
    Project,
    Full
}

public sealed record RelationScope(RelationScopeKind Kind, string? Name)
{
    /// <summary>
    /// Parses specs like: type:Demo.Core.Order, project:Demo.App, full
    /// </summary>
    public static RelationScope Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException("Empty relations scope.");

        var s = spec.Trim();
        if (s.Equals("full", StringComparison.OrdinalIgnoreCase)
            || s.Equals("*", StringComparison.OrdinalIgnoreCase))
            return new RelationScope(RelationScopeKind.Full, null);

        var colon = s.IndexOf(':');
        if (colon <= 0 || colon == s.Length - 1)
            throw new ArgumentException(
                $"Invalid relations scope '{spec}'. Use type:Name, project:Name, or full.");

        var kind = s[..colon].Trim();
        var name = s[(colon + 1)..].Trim();
        if (name.Length == 0)
            throw new ArgumentException($"Invalid relations scope '{spec}'.");

        return kind.ToLowerInvariant() switch
        {
            "type" or "class" => new RelationScope(RelationScopeKind.Type, name),
            "project" or "proj" => new RelationScope(RelationScopeKind.Project, name),
            _ => throw new ArgumentException(
                $"Unknown relations scope kind '{kind}'. Use type, project, or full.")
        };
    }

    public static IReadOnlyList<RelationScope> ParseMany(IEnumerable<string> specs) =>
        specs.Select(Parse).ToList();

    public override string ToString() => Kind switch
    {
        RelationScopeKind.Full => "full",
        RelationScopeKind.Type => $"type:{Name}",
        RelationScopeKind.Project => $"project:{Name}",
        _ => Kind.ToString()
    };
}
