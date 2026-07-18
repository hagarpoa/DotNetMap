using Microsoft.CodeAnalysis;

namespace DotNetMap.Core.Extraction;

public static class Visibility
{
    public static string ToString(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        _ => accessibility.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// Default: public + internal. With includePrivate: all.
    /// Nested private types of public parents still require includePrivate.
    /// </summary>
    public static bool IsIncluded(Accessibility accessibility, bool includePrivate)
    {
        if (includePrivate)
            return accessibility is not Accessibility.NotApplicable;

        return accessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.ProtectedOrInternal;
    }

    public static bool LooksLikeTestProject(string projectName, string? projectPath)
    {
        if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || projectName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
            || projectName.Contains("Tests", StringComparison.OrdinalIgnoreCase)
            || projectName.EndsWith("Test", StringComparison.OrdinalIgnoreCase))
            return true;

        if (projectPath is null)
            return false;

        var normalized = projectPath.Replace('\\', '/');
        return normalized.Contains("/Tests/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains(".Tests/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Always skipped paths (bin/obj). Not overridable.
    /// </summary>
    public static bool ShouldSkipSourcePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var n = path.Replace('\\', '/');
        return n.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || n.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || n.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Heuristic: source-generator / designer / assembly-info style files (DNM-017).
    /// </summary>
    public static bool IsGeneratedSourcePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var n = path.Replace('\\', '/');
        var file = Path.GetFileName(n);

        if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith("GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
            return true;

        // Common generator output folders
        if (n.Contains("/Generated/", StringComparison.OrdinalIgnoreCase)
            || n.Contains("/.generated/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// True when the file should not be indexed under current options.
    /// </summary>
    public static bool ShouldSkipDocument(string? path, bool includeGenerated)
    {
        if (ShouldSkipSourcePath(path))
            return true;
        if (!includeGenerated && IsGeneratedSourcePath(path))
            return true;
        return false;
    }

    /// <summary>
    /// Symbol-level generated detection: attributes + compiler flags (DNM-017).
    /// </summary>
    public static bool IsGeneratedSymbol(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared)
            return true;

        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name is "GeneratedCodeAttribute" or "CompilerGeneratedAttribute")
                return true;

            var full = attr.AttributeClass?.ToDisplayString();
            if (full is "System.CodeDom.Compiler.GeneratedCodeAttribute"
                or "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                return true;
        }

        // Check containing type attributes (nested in generated type)
        if (symbol.ContainingType is { } parent
            && !SymbolEqualityComparer.Default.Equals(parent, symbol)
            && IsGeneratedSymbolShallow(parent))
            return true;

        return false;
    }

    private static bool IsGeneratedSymbolShallow(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared)
            return true;
        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name is "GeneratedCodeAttribute" or "CompilerGeneratedAttribute")
                return true;
        }

        return false;
    }
}
