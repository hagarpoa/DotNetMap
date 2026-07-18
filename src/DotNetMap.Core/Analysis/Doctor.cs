using System.Text;
using System.Text.Json;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;
using Microsoft.Build.Locator;

namespace DotNetMap.Core.Analysis;

/// <summary>
/// Environment + index health checklist (DNM-034).
/// </summary>
public static class Doctor
{
    public sealed record Check(string Name, bool Pass, string Detail, string Severity = "error");

    public sealed class Report
    {
        public List<Check> Checks { get; } = [];
        public bool Ok => Checks.Where(c => c.Severity == "error").All(c => c.Pass);
        public int PassCount => Checks.Count(c => c.Pass);
        public int FailCount => Checks.Count(c => !c.Pass);
    }

    public static Report Run(string? dbPath = null)
    {
        var report = new Report();

        // .NET / runtime
        report.Checks.Add(new Check(
            "dotnet_runtime",
            true,
            $".NET {Environment.Version} ({System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription})"));

        // MSBuild (DNM-024)
        try
        {
            WorkspaceLoader.EnsureMsBuildRegistered();
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            var registered = MSBuildLocator.IsRegistered;
            var pathHint = WorkspaceLoader.RegisteredMsBuildPath;
            var detail = registered
                ? $"MSBuild registered ({instances.Count} VS instance(s) visible)"
                  + (pathHint is null ? "" : $"; path={pathHint}")
                : instances.Count > 0
                    ? $"{instances.Count} MSBuild instance(s) found"
                    : "No MSBuild/VS instance found — install .NET SDK / Visual Studio Build Tools. See docs/TROUBLESHOOTING.md";
            report.Checks.Add(new Check(
                "msbuild",
                registered || instances.Count > 0,
                detail,
                "error"));

            // global.json near cwd (warn only)
            var gjson = Path.Combine(Directory.GetCurrentDirectory(), "global.json");
            if (File.Exists(gjson))
            {
                report.Checks.Add(new Check(
                    "global_json",
                    true,
                    $"Found {gjson} — ensure pinned SDK is installed (dotnet --list-sdks)",
                    "info"));
            }
        }
        catch (Exception ex)
        {
            report.Checks.Add(new Check(
                "msbuild",
                false,
                ex.Message + " | " + WorkspaceLoader.MsBuildTroubleshootingHint()));
        }

        // DB
        dbPath ??= Path.Combine(Directory.GetCurrentDirectory(), ".dotnetmap", "index.db");
        var dbFull = Path.GetFullPath(dbPath);
        if (!File.Exists(dbFull))
        {
            report.Checks.Add(new Check(
                "database",
                false,
                $"No database at {dbFull}. Run: dotnetmap index <solution> --db {dbPath}",
                "warn"));
            report.Checks.Add(new Check("schema", true, "skipped (no db)", "info"));
            report.Checks.Add(new Check("index_data", true, "skipped (no db)", "info"));
            report.Checks.Add(new Check("staleness", true, "skipped (no db)", "info"));
            report.Checks.Add(new Check("quality", true, "skipped (no db)", "info"));
            return report;
        }

        report.Checks.Add(new Check("database", true, dbFull));

        try
        {
            using var store = MapStore.Open(dbFull);
            var status = store.GetStatus();
            var schemaOk = status.SchemaVersion >= 0;
            report.Checks.Add(new Check(
                "schema",
                schemaOk,
                schemaOk ? $"schema_version={status.SchemaVersion}" : "invalid schema_version"));

            var hasData = store.HasSolutionData() && status.TypeCount > 0;
            report.Checks.Add(new Check(
                "index_data",
                hasData,
                hasData
                    ? $"{status.SolutionName}: {status.ProjectCount} projects, {status.TypeCount} types, {status.MemberCount} members"
                    : "Database empty — run index",
                hasData ? "error" : "warn"));

            if (hasData)
            {
                if (!string.IsNullOrEmpty(status.SolutionPath))
                {
                    var solExists = File.Exists(status.SolutionPath) || Directory.Exists(status.SolutionPath);
                    report.Checks.Add(new Check(
                        "solution_path",
                        solExists,
                        solExists ? status.SolutionPath! : $"Missing: {status.SolutionPath}"));
                }

                var stale = IndexStaleness.Check(store);
                report.Checks.Add(new Check(
                    "staleness",
                    !stale.IsStale,
                    stale.IsStale
                        ? $"STALE: {stale.StaleProjects.Count} project(s) — {string.Join(", ", stale.StaleProjects.Take(5))}"
                        : "Index matches disk (project fingerprints)",
                    stale.IsStale ? "warn" : "info"));

                var quality = IndexQuality.Compute(store);
                report.Checks.Add(new Check(
                    "quality",
                    quality.Grade is "A" or "B" or "C",
                    $"grade={quality.Grade}; summaries={quality.TypesWithSummaryPercent}%; methods_with_calls={quality.MethodsWithCallsPercent}%",
                    "info"));
            }
            else
            {
                report.Checks.Add(new Check("staleness", true, "skipped", "info"));
                report.Checks.Add(new Check("quality", true, "skipped", "info"));
            }
        }
        catch (Exception ex)
        {
            report.Checks.Add(new Check("database_open", false, ex.Message));
        }

        // Write permission on db dir
        try
        {
            var dir = Path.GetDirectoryName(dbFull)!;
            var probe = Path.Combine(dir, $".dotnetmap-doctor-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            report.Checks.Add(new Check("db_writable", true, dir));
        }
        catch (Exception ex)
        {
            report.Checks.Add(new Check("db_writable", false, ex.Message, "warn"));
        }

        return report;
    }

    public static string FormatMarkdown(Report report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DotNetMap doctor");
        sb.AppendLine();
        sb.AppendLine($"Overall: **{(report.Ok ? "OK" : "ISSUES")}** ({report.PassCount} pass, {report.FailCount} fail)");
        sb.AppendLine();
        foreach (var c in report.Checks)
        {
            var mark = c.Pass ? "OK" : "FAIL";
            sb.AppendLine($"- [{mark}] **{c.Name}** ({c.Severity}): {c.Detail}");
        }

        sb.AppendLine();
        if (!report.Ok)
        {
            sb.AppendLine("## Hints");
            sb.AppendLine("- Install .NET 10 SDK and VS Build Tools if MSBuild fails.");
            sb.AppendLine("- Run `dotnetmap index <solution> --db .dotnetmap/index.db` to create/fill the index.");
            sb.AppendLine("- If stale: `dotnetmap index <solution> --changed-only`.");
        }

        return sb.ToString();
    }

    public static string FormatJson(Report report) =>
        JsonSerializer.Serialize(new
        {
            ok = report.Ok,
            passCount = report.PassCount,
            failCount = report.FailCount,
            checks = report.Checks
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
}
