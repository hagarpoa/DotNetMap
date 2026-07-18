using DotNetMap.Core.Extraction;
using DotNetMap.Core.Source;
using DotNetMap.Core.Store;

namespace DotNetMap.Core.Analysis;

/// <summary>
/// Detects whether the on-disk solution diverged from the index (DNM-009).
/// Project-level: csproj/source hash change, missing file, or new .cs under the project.
/// Does not open MSBuildWorkspace (suitable for status).
/// </summary>
public static class IndexStaleness
{
    public sealed class Report
    {
        public string? SolutionPath { get; set; }
        public bool SolutionPathExists { get; set; } = true;
        public List<string> OkProjects { get; } = [];
        public List<string> StaleProjects { get; } = [];
        public List<string> MissingFiles { get; } = [];
        public List<string> NewFiles { get; } = [];
        public List<string> Details { get; } = [];

        public bool IsStale =>
            !SolutionPathExists
            || StaleProjects.Count > 0
            || MissingFiles.Count > 0
            || NewFiles.Count > 0;
    }

    public static Report Check(MapStore store)
    {
        var report = new Report
        {
            SolutionPath = store.GetStatus().SolutionPath
        };

        if (string.IsNullOrEmpty(report.SolutionPath))
        {
            report.Details.Add("No solution_path in index.");
            report.SolutionPathExists = false;
            return report;
        }

        report.SolutionPathExists =
            File.Exists(report.SolutionPath) || Directory.Exists(report.SolutionPath);
        if (!report.SolutionPathExists)
        {
            report.Details.Add($"Solution path not found on disk: {report.SolutionPath}");
            return report;
        }

        var map = store.LoadFullMap();
        if (map is null || map.Projects.Count == 0)
        {
            report.Details.Add("Index has no projects.");
            return report;
        }

        var solutionRoot = SourceSnippetReader.ResolveSolutionRoot(report.SolutionPath);

        foreach (var project in map.Projects)
        {
            var reasons = new List<string>();

            if (!string.IsNullOrEmpty(project.Path) && File.Exists(project.Path))
            {
                var currentProjHash = ContentHasher.Sha256File(project.Path);
                if (!string.IsNullOrEmpty(project.FileHash)
                    && !string.Equals(currentProjHash, project.FileHash, StringComparison.Ordinal))
                {
                    reasons.Add("csproj changed");
                }
            }
            else if (!string.IsNullOrEmpty(project.Path))
            {
                reasons.Add("csproj missing");
            }

            foreach (var file in project.Files)
            {
                var abs = file.AbsolutePath;
                if (string.IsNullOrEmpty(abs) || !File.Exists(abs))
                    abs = store.ResolveFileAbsolutePath(file.RelativePath) ?? abs;

                if (string.IsNullOrEmpty(abs) || !File.Exists(abs))
                {
                    report.MissingFiles.Add(file.RelativePath);
                    reasons.Add($"missing {file.RelativePath}");
                    continue;
                }

                try
                {
                    // Match StructureExtractor hashing: UTF-8 of full text content
                    var text = File.ReadAllText(abs);
                    var hash = ContentHasher.Sha256Hex(text);
                    if (!string.Equals(hash, file.ContentHash, StringComparison.Ordinal))
                        reasons.Add($"changed {file.RelativePath}");
                }
                catch (Exception ex)
                {
                    reasons.Add($"unreadable {file.RelativePath}: {ex.Message}");
                }
            }

            // New .cs files under project directory not present in index
            var projDir = !string.IsNullOrEmpty(project.Path) && File.Exists(project.Path)
                ? Path.GetDirectoryName(project.Path)
                : null;

            if (!string.IsNullOrEmpty(projDir) && Directory.Exists(projDir))
            {
                foreach (var cs in Directory.EnumerateFiles(projDir, "*.cs", SearchOption.AllDirectories))
                {
                    // Skip bin/obj and generated noise for staleness of hand-written sources
                    if (Visibility.ShouldSkipDocument(cs, includeGenerated: false))
                        continue;

                    var full = Path.GetFullPath(cs);
                    var alreadyIndexed = project.Files.Any(f =>
                        !string.IsNullOrEmpty(f.AbsolutePath)
                        && string.Equals(Path.GetFullPath(f.AbsolutePath), full, StringComparison.OrdinalIgnoreCase));

                    if (alreadyIndexed)
                        continue;

                    string relDisplay;
                    try
                    {
                        relDisplay = solutionRoot is not null
                            ? Path.GetRelativePath(solutionRoot, full).Replace('\\', '/')
                            : Path.GetRelativePath(projDir, full).Replace('\\', '/');
                    }
                    catch
                    {
                        relDisplay = Path.GetFileName(full);
                    }

                    // Also match by relative path string stored in index
                    if (project.Files.Any(f =>
                            string.Equals(
                                f.RelativePath.Replace('\\', '/'),
                                relDisplay,
                                StringComparison.OrdinalIgnoreCase)))
                        continue;

                    report.NewFiles.Add(relDisplay);
                    reasons.Add($"new file {relDisplay}");
                }
            }

            if (reasons.Count > 0)
            {
                report.StaleProjects.Add(project.Name);
                report.Details.Add(
                    $"{project.Name}: {string.Join("; ", reasons.Take(5))}"
                    + (reasons.Count > 5 ? "…" : ""));
            }
            else
            {
                report.OkProjects.Add(project.Name);
            }
        }

        return report;
    }
}
