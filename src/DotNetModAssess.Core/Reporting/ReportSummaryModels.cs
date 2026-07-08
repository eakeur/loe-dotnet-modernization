namespace DotNetModAssess.Core.Reporting;

/// <summary>
/// Purpose-built, shareable summary of a solution assessment. This is the payload written by
/// <see cref="MarkdownJsonReportExporter"/> for both the Markdown and JSON export formats — it is
/// deliberately not a raw dump of <c>SolutionModel</c>/<c>DependencyGraph</c>, since those graphs
/// contain internal cross-references (e.g. <c>ProjectModel.ProjectReferences</c> pointing back at
/// full sibling objects) that are not meaningful to a stakeholder-facing report.
/// </summary>
public sealed class SolutionReportSummary
{
    public required string SolutionPath { get; init; }
    public required int ProjectCount { get; init; }

    /// <summary>
    /// Number of projects declaring each target framework moniker. A multi-targeted project
    /// contributes one count to each of its TFMs.
    /// </summary>
    public required IReadOnlyDictionary<string, int> TargetFrameworkDistribution { get; init; }

    public required int SdkStyleProjectCount { get; init; }
    public required int LegacyStyleProjectCount { get; init; }

    public required IReadOnlyList<PackageConflictSummary> PackageVersionConflicts { get; init; }
    public required IReadOnlyList<ProjectEvaluationErrorSummary> ProjectEvaluationErrors { get; init; }
    public required IReadOnlyList<ProjectDiagnosticSummary> ProjectDiagnostics { get; init; }
    public required LegacyCouplingSummary LegacyCoupling { get; init; }
    public required UsageResultsSummary UsageResults { get; init; }
}

public sealed record PackageProjectVersionSummary(string ProjectPath, string ProjectName, string Version);

public sealed class PackageConflictSummary
{
    public required string PackageId { get; init; }
    public required IReadOnlyList<PackageProjectVersionSummary> ProjectVersions { get; init; }
}

public sealed class ProjectEvaluationErrorSummary
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }
    public required string ErrorMessage { get; init; }
}

public sealed class ProjectDiagnosticSummary
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? File { get; init; }
    public int? LineNumber { get; init; }
}

/// <summary>
/// Counts of legacy-coupling signals (see <c>LegacyCouplingSignals</c>) across all projects in the
/// solution, plus the packages.config count, giving a quick read on how much legacy-Framework
/// coupling stands between the codebase and a Linux-container-friendly modern .NET target.
/// </summary>
public sealed class LegacyCouplingSummary
{
    public required int ProjectsReferencingSystemWeb { get; init; }
    public required int ProjectsReferencingSystemServiceModel { get; init; }
    public required int ProjectsReferencingSystemMessaging { get; init; }
    public required int ProjectsWithComReferences { get; init; }
    public required int ProjectsWithPInvokeSignals { get; init; }
    public required int ProjectsUsingConfigurationManager { get; init; }
    public required int ProjectsWithAppConfig { get; init; }
    public required int ProjectsWithWebConfig { get; init; }
    public required int ProjectsUsingPackagesConfig { get; init; }
}

public sealed record SymbolUsageCount(string MatchedSymbol, int Count);

public sealed record ProjectUsageCount(string? ProjectPath, int Count);

public sealed class UsageResultsSummary
{
    public required int TotalUsageCount { get; init; }
    public required IReadOnlyList<SymbolUsageCount> CountsBySymbol { get; init; }
    public required IReadOnlyList<ProjectUsageCount> CountsByProject { get; init; }
}
