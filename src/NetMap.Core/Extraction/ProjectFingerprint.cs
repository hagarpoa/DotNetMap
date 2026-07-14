using Microsoft.CodeAnalysis;
using NetMap.Core.Domain;

namespace NetMap.Core.Extraction;

/// <summary>
/// Project-level fingerprint: csproj hash + sorted source file content hashes.
/// Any file change invalidates the whole project (documented MVP semantics).
/// Must match hashing used during structure extraction.
/// </summary>
public static class ProjectFingerprint
{
    public static async Task<string> ComputeAsync(
        Project project,
        string solutionDir,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();

        if (project.FilePath is not null && File.Exists(project.FilePath))
            parts.Add("proj:" + ContentHasher.Sha256File(project.FilePath));
        else
            parts.Add("proj:" + project.Name);

        var fileHashes = new List<string>();
        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Visibility.ShouldSkipSourcePath(document.FilePath) || document.FilePath is null)
                continue;

            // Same as StructureExtractor: hash document text (UTF-16 source text as string bytes via UTF-8)
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var hash = ContentHasher.Sha256Hex(text.ToString());
            var relative = GetRelativePath(solutionDir, document.FilePath).Replace('\\', '/');
            fileHashes.Add($"{relative}:{hash}");
        }

        fileHashes.Sort(StringComparer.OrdinalIgnoreCase);
        parts.AddRange(fileHashes);

        return ContentHasher.Sha256Hex(string.Join("\n", parts));
    }

    public static string ComputeFromNode(ProjectNode project)
    {
        var parts = new List<string>
        {
            "proj:" + (project.FileHash ?? project.Name)
        };

        foreach (var f in project.Files.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
            parts.Add($"{f.RelativePath.Replace('\\', '/')}:{f.ContentHash}");

        return ContentHasher.Sha256Hex(string.Join("\n", parts));
    }

    private static string GetRelativePath(string root, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(root, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }
}
