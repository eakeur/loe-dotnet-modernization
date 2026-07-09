using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Mcp.Dtos;

/// <summary>
/// Plain, MCP-serializable projections of Core domain types. Mirrors the pattern
/// `DependencyGraphView.razor` already uses in the Web project (projecting `GraphNode`/`GraphEdge`
/// into small local DTOs for JS interop) - here the "consumer" is JSON-RPC/the MCP SDK's schema
/// generator instead of JS, but the reason is the same: several Core model types carry behavior
/// (e.g. `DependencyGraph` has methods) or nested object-graph cycles/duplication (e.g.
/// `ProjectModel.ProjectReferences` holding full nested `ProjectModel` instances) that don't
/// project cleanly into a flat JSON tool result, so we flatten explicitly rather than fighting the
/// domain model's shape.
/// </summary>
public sealed record SolutionOverviewDto(
    string SolutionPath,
    int ProjectCount,
    int SdkStyleProjectCount,
    int LegacyStyleProjectCount,
    int PackagesConfigProjectCount,
    int CentrallyManagedProjectCount,
    int NuGetSourceCount,
    int DistinctPackageCount,
    int PackageVersionConflictCount,
    bool IsWatching,
    IReadOnlyList<TfmCountDto> TargetFrameworkBreakdown,
    IReadOnlyList<PatternCountDto> LegacyFindingsByPattern);

public sealed record TfmCountDto(string TargetFramework, int Count);

public sealed record PatternCountDto(string PatternName, int Count);

/// <summary>One row in `list_projects` - the key metadata flags an agent needs to decide whether a
/// project is interesting, without the full raw-properties bag from `get_project_details`.</summary>
public sealed record ProjectSummaryDto(
    string Path,
    string Name,
    ProjectFormat Format,
    string TargetFrameworkRaw,
    TargetFrameworkClassification TargetFrameworkClassification,
    ProjectOutputType OutputType,
    PackagesModel PackagesModel,
    bool IsCentrallyManaged,
    bool IsTestProject,
    int ProjectReferenceCount,
    int PackageReferenceCount,
    bool ReferencesSystemWeb,
    bool ReferencesSystemServiceModel,
    bool ReferencesSystemMessaging,
    bool UsesConfigurationManager,
    bool UseWindowsForms,
    bool UseWpf,
    bool HasComReferences,
    bool HasPInvokeSignals,
    bool EvaluationSucceeded);

public sealed record ProjectReferenceDto(string Path, string Name);

public sealed record PackageReferenceDto(
    string PackageId,
    string? RequestedVersion,
    string? ResolvedVersion,
    bool IsCentrallyManaged,
    bool IsFloatingVersion,
    bool HasVersionConflictAcrossSolution);

/// <summary>Full detail for one project, returned by `get_project_details` - the flattened
/// `ProjectMetadata` plus its graph neighbors (dependencies/dependents), which the raw
/// `ProjectModel` doesn't carry (dependents require the whole-solution `DependencyGraph`).</summary>
public sealed record ProjectDetailsDto(
    string Path,
    string Name,
    ProjectFormat Format,
    ProjectOutputType OutputType,
    IReadOnlyList<string> TargetFrameworks,
    string TargetFrameworkRaw,
    TargetFrameworkClassification TargetFrameworkClassification,
    PackagesModel PackagesModel,
    bool IsCentrallyManaged,
    string? LangVersion,
    string? Nullable,
    bool? ImplicitUsings,
    bool? AllowUnsafeBlocks,
    bool? TreatWarningsAsErrors,
    IReadOnlyList<string> DefineConstants,
    string? AssemblyName,
    string? RootNamespace,
    IReadOnlyList<string> RuntimeIdentifiers,
    string? PlatformTarget,
    bool UseWindowsForms,
    bool UseWpf,
    bool? IsPackable,
    bool IsTestProject,
    string? TestFramework,
    LegacyCouplingSignals LegacySignals,
    IReadOnlyList<string> DirectoryBuildPropsChain,
    IReadOnlyList<string> DirectoryBuildTargetsChain,
    IReadOnlyList<CustomBuildElement> CustomTargetsAndImports,
    IReadOnlyList<EvaluationDiagnostic> Diagnostics,
    bool EvaluationSucceeded,
    string? EvaluationErrorMessage,
    IReadOnlyList<PackageReferenceDto> PackageReferences,
    IReadOnlyList<ProjectReferenceDto> Dependencies,
    IReadOnlyList<ProjectReferenceDto> Dependents);

/// <summary>One distinct package across the whole solution, returned by `list_packages`.</summary>
public sealed record PackageSummaryDto(
    string PackageId,
    bool HasVersionConflict,
    IReadOnlyList<PackageProjectVersion> ProjectVersions);

public sealed record UsageResultDto(
    string TargetName,
    string FilePath,
    int LineNumber,
    string MatchedSymbol,
    UsageReferenceKind Kind,
    UsageConfidence Confidence,
    string? ProjectPath,
    string? CodeSnippet);

public sealed record GraphNodeDto(string Id, string Kind, string Label, bool HasVersionConflict, IReadOnlyList<string> TargetFrameworks);

public sealed record GraphEdgeDto(string Kind, string FromId, string ToId, string? ResolvedPackageVersion);

public sealed record DependencyGraphDto(IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges);
