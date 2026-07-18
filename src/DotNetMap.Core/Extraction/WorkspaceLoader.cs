using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNetMap.Core.Extraction;

public sealed class WorkspaceLoader : IDisposable
{
    private static readonly object LocatorGate = new();
    private static bool _locatorRegistered;
    private static string? _registeredMsBuildPath;

    private readonly MSBuildWorkspace _workspace;
    private readonly List<string> _diagnostics = [];

    private WorkspaceLoader(MSBuildWorkspace workspace)
    {
        _workspace = workspace;
        _workspace.RegisterWorkspaceFailedHandler(e =>
        {
            var msg = e.Diagnostic.Message;
            if (IsNoisyWorkspaceDiagnostic(msg))
                return;
            _diagnostics.Add($"{e.Diagnostic.Kind}: {msg}");
        });
    }

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public MSBuildWorkspace Workspace => _workspace;

    /// <summary>MSBuild path registered by Locator (if known).</summary>
    public static string? RegisteredMsBuildPath => _registeredMsBuildPath;

    public static void EnsureMsBuildRegistered()
    {
        lock (LocatorGate)
        {
            if (_locatorRegistered || MSBuildLocator.IsRegistered)
            {
                _locatorRegistered = true;
                return;
            }

            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            if (instances.Length > 0)
            {
                // Prefer newest instance with a usable MSBuild
                var best = instances.OrderByDescending(i => i.Version).First();
                MSBuildLocator.RegisterInstance(best);
                _registeredMsBuildPath = best.MSBuildPath;
            }
            else
            {
                try
                {
                    MSBuildLocator.RegisterDefaults();
                    _registeredMsBuildPath = "(MSBuildLocator defaults — .NET SDK)";
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Could not register MSBuild. Install the .NET SDK and/or Visual Studio Build Tools, " +
                        "then retry. Details: " + ex.Message + Environment.NewLine +
                        MsBuildTroubleshootingHint(),
                        ex);
                }
            }

            _locatorRegistered = true;
        }
    }

    public static WorkspaceLoader Create()
    {
        EnsureMsBuildRegistered();

        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            // Prefer DesignTimeBuild when available; keep restore out of workspace when possible
            ["AlwaysCompileMarkupFilesInSeparateDomain"] = "false",
            ["CheckForSystemRuntimeDependency"] = "true"
        });

        return new WorkspaceLoader(workspace);
    }

    /// <summary>
    /// Resolves a path to a solution (.sln/.slnx) or project (.csproj) absolute path.
    /// </summary>
    public static string ResolveEntryPath(string path)
    {
        var full = Path.GetFullPath(path);

        if (File.Exists(full))
        {
            var ext = Path.GetExtension(full);
            if (ext is ".sln" or ".slnx" or ".csproj")
                return full;
            throw new InvalidOperationException($"Unsupported file type: {ext}. Use .sln, .slnx, or .csproj.");
        }

        if (!Directory.Exists(full))
            throw new FileNotFoundException("Path not found.", full);

        var slnx = Directory.GetFiles(full, "*.slnx", SearchOption.TopDirectoryOnly);
        if (slnx.Length == 1)
            return slnx[0];
        if (slnx.Length > 1)
            throw new InvalidOperationException(
                $"Multiple .slnx files in {full}. Pass the solution path explicitly.");

        var sln = Directory.GetFiles(full, "*.sln", SearchOption.TopDirectoryOnly);
        if (sln.Length == 1)
            return sln[0];
        if (sln.Length > 1)
            throw new InvalidOperationException(
                $"Multiple .sln files in {full}. Pass the solution path explicitly.");

        var csproj = Directory.GetFiles(full, "*.csproj", SearchOption.TopDirectoryOnly);
        if (csproj.Length == 1)
            return csproj[0];
        if (csproj.Length > 1)
            throw new InvalidOperationException(
                $"Multiple .csproj files in {full} and no solution. Pass a path explicitly.");

        throw new FileNotFoundException(
            $"No .sln, .slnx, or .csproj found in {full}.");
    }

    public async Task<Solution> OpenAsync(string entryPath, CancellationToken cancellationToken = default)
    {
        var path = ResolveEntryPath(entryPath);
        var ext = Path.GetExtension(path);

        try
        {
            if (ext is ".sln" or ".slnx")
                return await _workspace.OpenSolutionAsync(path, progress: null, cancellationToken)
                    .ConfigureAwait(false);

            if (ext is ".csproj")
            {
                var project = await _workspace.OpenProjectAsync(path, progress: null, cancellationToken)
                    .ConfigureAwait(false);
                return project.Solution;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(FormatOpenFailure(path, ex), ex);
        }

        throw new InvalidOperationException($"Unsupported entry: {path}");
    }

    /// <summary>User-facing MSBuild / workspace failure message (DNM-024).</summary>
    public static string FormatOpenFailure(string path, Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Failed to open workspace for '{path}': {ex.Message}");

        var text = ex.ToString();
        if (LooksLikeMissingSdk(text))
        {
            sb.AppendLine("Hint: .NET SDK not found or wrong version for this machine.");
            sb.AppendLine("  - Install SDK: https://dotnet.microsoft.com/download");
            sb.AppendLine("  - Check: dotnet --list-sdks");
        }

        if (LooksLikeRestoreNeeded(text))
        {
            sb.AppendLine("Hint: restore NuGet packages, then re-run index:");
            sb.AppendLine($"  dotnet restore \"{path}\"");
        }

        if (LooksLikeGlobalJson(text) || File.Exists(FindGlobalJsonNear(path)))
        {
            sb.AppendLine("Hint: a global.json may pin an SDK that is not installed.");
            sb.AppendLine("  - Install the pinned SDK or adjust global.json");
        }

        sb.AppendLine(MsBuildTroubleshootingHint());
        return sb.ToString().TrimEnd();
    }

    public static string MsBuildTroubleshootingHint() =>
        "See docs/TROUBLESHOOTING.md — MSBuild via MSBuildLocator (VS Build Tools or .NET SDK).";

    public static bool IsNoisyWorkspaceDiagnostic(string message)
    {
        // Common design-time noise that does not block structure index
        if (message.Contains("MSB3277", StringComparison.OrdinalIgnoreCase)) // binding redirects
            return true;
        if (message.Contains("Found conflicts between different versions", StringComparison.OrdinalIgnoreCase))
            return true;
        if (message.Contains("could not be resolved because it has an indirect dependency", StringComparison.OrdinalIgnoreCase)
            && message.Contains("System.Runtime", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool LooksLikeMissingSdk(string text) =>
        text.Contains("SDK", StringComparison.OrdinalIgnoreCase)
        && (text.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("was not found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("No .NET SDKs", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeRestoreNeeded(string text) =>
        text.Contains("restore", StringComparison.OrdinalIgnoreCase)
        || text.Contains("NuGet", StringComparison.OrdinalIgnoreCase)
        || text.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase)
        || text.Contains("packages are missing", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeGlobalJson(string text) =>
        text.Contains("global.json", StringComparison.OrdinalIgnoreCase);

    private static string? FindGlobalJsonNear(string path)
    {
        try
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            while (!string.IsNullOrEmpty(dir))
            {
                var g = Path.Combine(dir, "global.json");
                if (File.Exists(g))
                    return g;
                dir = Directory.GetParent(dir)?.FullName;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public void Dispose() => _workspace.Dispose();
}
