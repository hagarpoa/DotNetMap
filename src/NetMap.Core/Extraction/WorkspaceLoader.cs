using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace NetMap.Core.Extraction;

public sealed class WorkspaceLoader : IDisposable
{
    private static readonly object LocatorGate = new();
    private static bool _locatorRegistered;

    private readonly MSBuildWorkspace _workspace;
    private readonly List<string> _diagnostics = [];

    private WorkspaceLoader(MSBuildWorkspace workspace)
    {
        _workspace = workspace;
        _workspace.RegisterWorkspaceFailedHandler(e =>
        {
            _diagnostics.Add($"{e.Diagnostic.Kind}: {e.Diagnostic.Message}");
        });
    }

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public MSBuildWorkspace Workspace => _workspace;

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
            }
            else
            {
                MSBuildLocator.RegisterDefaults();
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

        if (ext is ".sln" or ".slnx")
            return await _workspace.OpenSolutionAsync(path, progress: null, cancellationToken).ConfigureAwait(false);

        if (ext is ".csproj")
        {
            var project = await _workspace.OpenProjectAsync(path, progress: null, cancellationToken).ConfigureAwait(false);
            return project.Solution;
        }

        throw new InvalidOperationException($"Unsupported entry: {path}");
    }

    public void Dispose() => _workspace.Dispose();
}
