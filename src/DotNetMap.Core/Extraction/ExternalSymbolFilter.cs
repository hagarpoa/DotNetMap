using Microsoft.CodeAnalysis;

namespace DotNetMap.Core.Extraction;

/// <summary>
/// Classifies symbols as solution-local vs external (BCL / NuGet) for DNM-007.
/// </summary>
public static class ExternalSymbolFilter
{
    private static readonly HashSet<string> ExternalAssemblyPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "System.",
        "Microsoft.",
        "mscorlib",
        "netstandard",
        "WindowsBase",
        "PresentationFramework",
        "PresentationCore",
        "WindowsForms",
        "Newtonsoft.",
        "NUnit",
        "xunit",
        "Xunit",
        "Moq",
        "FluentAssertions"
    };

    /// <param name="solutionAssemblyNames">Assembly names of projects in the solution (case-insensitive).</param>
    public static bool IsSolutionLocal(ISymbol symbol, IReadOnlySet<string> solutionAssemblyNames)
    {
        var asm = symbol.ContainingAssembly;
        if (asm is null)
            return false;

        var name = asm.Name;
        if (solutionAssemblyNames.Contains(name))
            return true;

        // Metadata name without extension
        if (solutionAssemblyNames.Contains(asm.Identity.Name))
            return true;

        return false;
    }

    public static bool IsLikelyExternalAssembly(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        if (ExternalAssemblyPrefixes.Contains(assemblyName))
            return true;

        foreach (var prefix in ExternalAssemblyPrefixes)
        {
            if (prefix.EndsWith('.') && assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!prefix.EndsWith('.') && assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // System.* assemblies
        if (assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("System", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// True if symbol should be stored as a call/dep when external filtering is on.
    /// </summary>
    public static bool ShouldInclude(
        ISymbol symbol,
        IReadOnlySet<string> solutionAssemblyNames,
        bool includeExternal)
    {
        if (includeExternal)
            return true;

        if (IsSolutionLocal(symbol, solutionAssemblyNames))
            return true;

        var asmName = symbol.ContainingAssembly?.Name;
        // If not in solution assemblies and looks external → drop
        if (asmName is not null && IsLikelyExternalAssembly(asmName))
            return false;

        // Unknown third-party NuGet: treat as external when filtering
        return false;
    }
}
