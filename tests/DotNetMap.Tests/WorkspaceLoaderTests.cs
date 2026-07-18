using DotNetMap.Core.Extraction;

namespace DotNetMap.Tests;

public class WorkspaceLoaderTests
{
    [Fact]
    public void FormatOpenFailure_MentionsRestoreAndDocs()
    {
        var ex = new Exception("project.assets.json not found; run restore");
        var msg = WorkspaceLoader.FormatOpenFailure(@"C:\src\Demo.sln", ex);
        Assert.Contains("restore", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TROUBLESHOOTING", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsNoisyWorkspaceDiagnostic_FiltersMsb3277()
    {
        Assert.True(WorkspaceLoader.IsNoisyWorkspaceDiagnostic(
            "warning MSB3277: Found conflicts between different versions of the same dependent assembly."));
        Assert.False(WorkspaceLoader.IsNoisyWorkspaceDiagnostic(
            "error: The SDK 'Microsoft.NET.Sdk' specified could not be found."));
    }

    [Fact]
    public void ResolveEntryPath_FindsDemoSolution()
    {
        var demo = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "DemoSolution"));
        if (!Directory.Exists(demo))
            return; // skip if layout differs

        var entry = WorkspaceLoader.ResolveEntryPath(demo);
        Assert.True(entry.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                    || entry.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
    }
}
