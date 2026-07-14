namespace DotNetMap.Core.Export;

/// <summary>Output verbosity for AI-facing responses.</summary>
public enum DetailLevel
{
    /// <summary>Minimal tokens: calls only, short names, tight caps.</summary>
    Compact,

    /// <summary>Include signature deps, consumers, fuller lists.</summary>
    Full
}
