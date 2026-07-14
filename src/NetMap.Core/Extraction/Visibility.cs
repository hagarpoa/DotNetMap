using Microsoft.CodeAnalysis;

namespace NetMap.Core.Extraction;

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

    public static bool ShouldSkipSourcePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var n = path.Replace('\\', '/');
        if (n.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || n.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (n.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
            return true;

        // Generated / designer noise
        if (n.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
