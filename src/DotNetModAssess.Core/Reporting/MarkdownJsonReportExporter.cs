using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Reporting;

/// <summary>
/// Exports a stakeholder-facing solution summary report as either Markdown or JSON.
///
/// <para>
/// <b>Output path convention:</b> the format is selected by the extension of <paramref
/// name="outputPath"/> (case-insensitive): <c>.json</c> writes the purpose-built <see
/// cref="SolutionReportSummary"/> DTO as indented JSON; any other extension (typically
/// <c>.md</c>) writes a Markdown report covering the same facts. To produce both formats, call
/// <see cref="ExportAsync"/> twice with the extension swapped (e.g. <c>report.md</c> then
/// <c>report.json</c>) — the caller decides whether both are needed.
/// </para>
/// </summary>
public sealed class MarkdownJsonReportExporter : IReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task ExportAsync(
        SolutionModel solution,
        DependencyGraph graph,
        IReadOnlyList<UsageResult> usageResults,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var summary = BuildSummary(solution, graph, usageResults);
        var extension = Path.GetExtension(outputPath);

        var content = string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(summary, JsonOptions)
            : RenderMarkdown(summary);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, content, cancellationToken);
    }

    /// <summary>
    /// Builds the purpose-built summary DTO from the raw domain models. Exposed as a public
    /// static method so callers/tests can inspect the computed facts directly without parsing
    /// the rendered Markdown/JSON back out.
    /// </summary>
    public static SolutionReportSummary BuildSummary(
        SolutionModel solution,
        DependencyGraph graph,
        IReadOnlyList<UsageResult> usageResults)
    {
        var projectsByPath = solution.Projects.ToDictionary(p => p.Path);

        var tfmDistribution = solution.Projects
            .SelectMany(p => p.TargetFrameworks)
            .GroupBy(tfm => tfm)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        var sdkStyleCount = solution.Projects.Count(p => p.Format == ProjectFormat.SdkStyle);
        var legacyStyleCount = solution.Projects.Count(p => p.Format == ProjectFormat.LegacyStyle);

        var packageConflicts = graph.Nodes
            .OfType<PackageGraphNode>()
            .Where(n => n.Package.HasVersionConflict)
            .OrderBy(n => n.Package.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(n => new PackageConflictSummary
            {
                PackageId = n.Package.PackageId,
                ProjectVersions = n.Package.ProjectVersions
                    .Select(pv => new PackageProjectVersionSummary(
                        pv.ProjectPath,
                        projectsByPath.TryGetValue(pv.ProjectPath, out var proj) ? proj.Name : pv.ProjectPath,
                        pv.Version))
                    .ToList()
            })
            .ToList();

        var evaluationErrors = solution.Projects
            .Where(p => !p.Evaluation.Succeeded)
            .Select(p => new ProjectEvaluationErrorSummary
            {
                ProjectPath = p.Path,
                ProjectName = p.Name,
                ErrorMessage = p.Evaluation.ErrorMessage ?? "(no message)"
            })
            .ToList();

        var diagnostics = solution.Projects
            .SelectMany(p => p.Metadata.Diagnostics.Select(d => new ProjectDiagnosticSummary
            {
                ProjectPath = p.Path,
                ProjectName = p.Name,
                Severity = d.Severity.ToString(),
                Code = d.Code,
                Message = d.Message,
                File = d.File,
                LineNumber = d.LineNumber
            }))
            .ToList();

        var legacyCoupling = new LegacyCouplingSummary
        {
            ProjectsReferencingSystemWeb = solution.Projects.Count(p => p.Metadata.LegacySignals.ReferencesSystemWeb),
            ProjectsReferencingSystemServiceModel = solution.Projects.Count(p => p.Metadata.LegacySignals.ReferencesSystemServiceModel),
            ProjectsReferencingSystemMessaging = solution.Projects.Count(p => p.Metadata.LegacySignals.ReferencesSystemMessaging),
            ProjectsWithComReferences = solution.Projects.Count(p => p.Metadata.LegacySignals.ComReferences.Count > 0),
            ProjectsWithPInvokeSignals = solution.Projects.Count(p => p.Metadata.LegacySignals.HasPInvokeSignals),
            ProjectsUsingConfigurationManager = solution.Projects.Count(p => p.Metadata.LegacySignals.UsesConfigurationManager),
            ProjectsWithAppConfig = solution.Projects.Count(p => p.Metadata.LegacySignals.HasAppConfig),
            ProjectsWithWebConfig = solution.Projects.Count(p => p.Metadata.LegacySignals.HasWebConfig),
            ProjectsUsingPackagesConfig = solution.Projects.Count(p => p.Metadata.PackagesModel == PackagesModel.PackagesConfig)
        };

        var usageSummary = new UsageResultsSummary
        {
            TotalUsageCount = usageResults.Count,
            CountsBySymbol = usageResults
                .GroupBy(u => u.MatchedSymbol)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SymbolUsageCount(g.Key, g.Count()))
                .ToList(),
            CountsByProject = usageResults
                .GroupBy(u => u.ProjectPath)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProjectUsageCount(g.Key, g.Count()))
                .ToList()
        };

        return new SolutionReportSummary
        {
            SolutionPath = solution.Path,
            ProjectCount = solution.Projects.Count,
            TargetFrameworkDistribution = tfmDistribution,
            SdkStyleProjectCount = sdkStyleCount,
            LegacyStyleProjectCount = legacyStyleCount,
            PackageVersionConflicts = packageConflicts,
            ProjectEvaluationErrors = evaluationErrors,
            ProjectDiagnostics = diagnostics,
            LegacyCoupling = legacyCoupling,
            UsageResults = usageSummary
        };
    }

    private static string RenderMarkdown(SolutionReportSummary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Modernization Assessment Report");
        sb.AppendLine();
        sb.AppendLine($"**Solution:** `{summary.SolutionPath}`");
        sb.AppendLine();

        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine($"- **Project count:** {summary.ProjectCount}");
        sb.AppendLine($"- **SDK-style projects:** {summary.SdkStyleProjectCount}");
        sb.AppendLine($"- **Legacy-style projects:** {summary.LegacyStyleProjectCount}");
        sb.AppendLine();

        sb.AppendLine("### Target Framework Distribution");
        sb.AppendLine();
        sb.AppendLine("| Target Framework | Project Count |");
        sb.AppendLine("|---|---|");
        foreach (var (tfm, count) in summary.TargetFrameworkDistribution)
        {
            sb.AppendLine($"| {tfm} | {count} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Package Version Conflicts");
        sb.AppendLine();
        if (summary.PackageVersionConflicts.Count == 0)
        {
            sb.AppendLine("_No package version conflicts detected._");
        }
        else
        {
            foreach (var conflict in summary.PackageVersionConflicts)
            {
                sb.AppendLine($"### {conflict.PackageId}");
                sb.AppendLine();
                sb.AppendLine("| Project | Version |");
                sb.AppendLine("|---|---|");
                foreach (var pv in conflict.ProjectVersions)
                {
                    sb.AppendLine($"| {pv.ProjectName} (`{pv.ProjectPath}`) | {pv.Version} |");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Projects With Evaluation Errors");
        sb.AppendLine();
        if (summary.ProjectEvaluationErrors.Count == 0)
        {
            sb.AppendLine("_No project evaluation errors._");
        }
        else
        {
            foreach (var error in summary.ProjectEvaluationErrors)
            {
                sb.AppendLine($"- **{error.ProjectName}** (`{error.ProjectPath}`): {error.ErrorMessage}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Project Diagnostics");
        sb.AppendLine();
        if (summary.ProjectDiagnostics.Count == 0)
        {
            sb.AppendLine("_No project diagnostics reported._");
        }
        else
        {
            sb.AppendLine("| Project | Severity | Code | Message | File |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var diag in summary.ProjectDiagnostics)
            {
                var file = diag.File is null ? "" : diag.LineNumber is { } line ? $"{diag.File}:{line}" : diag.File;
                sb.AppendLine($"| {diag.ProjectName} | {diag.Severity} | {diag.Code} | {diag.Message} | {file} |");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Legacy Coupling Signals");
        sb.AppendLine();
        sb.AppendLine("| Signal | Project Count |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| References System.Web | {summary.LegacyCoupling.ProjectsReferencingSystemWeb} |");
        sb.AppendLine($"| References System.ServiceModel (WCF) | {summary.LegacyCoupling.ProjectsReferencingSystemServiceModel} |");
        sb.AppendLine($"| References System.Messaging (MSMQ) | {summary.LegacyCoupling.ProjectsReferencingSystemMessaging} |");
        sb.AppendLine($"| Has COM references | {summary.LegacyCoupling.ProjectsWithComReferences} |");
        sb.AppendLine($"| Has P/Invoke signals | {summary.LegacyCoupling.ProjectsWithPInvokeSignals} |");
        sb.AppendLine($"| Uses ConfigurationManager | {summary.LegacyCoupling.ProjectsUsingConfigurationManager} |");
        sb.AppendLine($"| Has app.config | {summary.LegacyCoupling.ProjectsWithAppConfig} |");
        sb.AppendLine($"| Has web.config | {summary.LegacyCoupling.ProjectsWithWebConfig} |");
        sb.AppendLine($"| Uses packages.config | {summary.LegacyCoupling.ProjectsUsingPackagesConfig} |");
        sb.AppendLine();

        sb.AppendLine("## Usage Results Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Total usage occurrences:** {summary.UsageResults.TotalUsageCount}");
        sb.AppendLine();
        sb.AppendLine("### By Matched Symbol");
        sb.AppendLine();
        sb.AppendLine("| Symbol | Count |");
        sb.AppendLine("|---|---|");
        foreach (var s in summary.UsageResults.CountsBySymbol)
        {
            sb.AppendLine($"| {s.MatchedSymbol} | {s.Count} |");
        }
        sb.AppendLine();
        sb.AppendLine("### By Project");
        sb.AppendLine();
        sb.AppendLine("| Project | Count |");
        sb.AppendLine("|---|---|");
        foreach (var p in summary.UsageResults.CountsByProject)
        {
            sb.AppendLine($"| {p.ProjectPath ?? "(unknown)"} | {p.Count} |");
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
