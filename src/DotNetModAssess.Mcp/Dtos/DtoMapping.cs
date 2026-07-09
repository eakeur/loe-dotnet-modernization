using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Mcp.Dtos;

/// <summary>Pure projection helpers from Core domain types to this project's flat, serializable
/// DTOs - see <see cref="Dtos"/>'s own file header for why these exist as a separate step rather
/// than serializing domain types directly.</summary>
public static class DtoMapping
{
    public static ProjectSummaryDto ToSummary(ProjectModel project) => new(
        Path: project.Path,
        Name: project.Name,
        Format: project.Format,
        TargetFrameworkRaw: project.Metadata.TargetFrameworkRaw,
        TargetFrameworkClassification: project.Metadata.TargetFrameworkClassification,
        OutputType: project.OutputType,
        PackagesModel: project.Metadata.PackagesModel,
        IsCentrallyManaged: project.Metadata.IsCentrallyManaged,
        IsTestProject: project.Metadata.IsTestProject,
        ProjectReferenceCount: project.ProjectReferences.Count,
        PackageReferenceCount: project.PackageReferences.Count,
        ReferencesSystemWeb: project.Metadata.LegacySignals.ReferencesSystemWeb,
        ReferencesSystemServiceModel: project.Metadata.LegacySignals.ReferencesSystemServiceModel,
        ReferencesSystemMessaging: project.Metadata.LegacySignals.ReferencesSystemMessaging,
        UsesConfigurationManager: project.Metadata.LegacySignals.UsesConfigurationManager,
        UseWindowsForms: project.Metadata.UseWindowsForms,
        UseWpf: project.Metadata.UseWpf,
        HasComReferences: project.Metadata.LegacySignals.ComReferences.Count > 0,
        HasPInvokeSignals: project.Metadata.LegacySignals.HasPInvokeSignals,
        EvaluationSucceeded: project.Evaluation.Succeeded);

    public static ProjectReferenceDto ToReference(ProjectModel project) => new(project.Path, project.Name);

    public static ProjectDetailsDto ToDetails(ProjectModel project, DependencyGraph? graph, SolutionModel solution)
    {
        var metadata = project.Metadata;

        var packageConflictsById = (graph?.Nodes.OfType<PackageGraphNode>() ?? [])
            .ToDictionary(n => n.Package.PackageId, n => n.Package.HasVersionConflict);

        var packageReferences = project.PackageReferences
            .Select(p => new PackageReferenceDto(
                p.PackageId,
                p.RequestedVersion,
                p.ResolvedVersion,
                p.IsCentrallyManaged,
                p.IsFloatingVersion,
                packageConflictsById.GetValueOrDefault(p.PackageId)))
            .ToList();

        var dependencies = project.ProjectReferences.Select(ToReference).ToList();

        var dependents = new List<ProjectReferenceDto>();
        if (graph is not null)
        {
            var projectsByPath = solution.Projects.ToDictionary(p => p.Path);
            dependents = graph.Edges
                .Where(e => e.Kind == GraphEdgeKind.ProjectToProject && e.ToId == project.Path)
                .Select(e => projectsByPath.GetValueOrDefault(e.FromId))
                .Where(p => p is not null)
                .Select(p => ToReference(p!))
                .ToList();
        }

        return new ProjectDetailsDto(
            Path: project.Path,
            Name: project.Name,
            Format: project.Format,
            OutputType: project.OutputType,
            TargetFrameworks: project.TargetFrameworks,
            TargetFrameworkRaw: metadata.TargetFrameworkRaw,
            TargetFrameworkClassification: metadata.TargetFrameworkClassification,
            PackagesModel: metadata.PackagesModel,
            IsCentrallyManaged: metadata.IsCentrallyManaged,
            LangVersion: metadata.LangVersion,
            Nullable: metadata.Nullable,
            ImplicitUsings: metadata.ImplicitUsings,
            AllowUnsafeBlocks: metadata.AllowUnsafeBlocks,
            TreatWarningsAsErrors: metadata.TreatWarningsAsErrors,
            DefineConstants: metadata.DefineConstants,
            AssemblyName: metadata.AssemblyName,
            RootNamespace: metadata.RootNamespace,
            RuntimeIdentifiers: metadata.RuntimeIdentifiers,
            PlatformTarget: metadata.PlatformTarget,
            UseWindowsForms: metadata.UseWindowsForms,
            UseWpf: metadata.UseWpf,
            IsPackable: metadata.IsPackable,
            IsTestProject: metadata.IsTestProject,
            TestFramework: metadata.TestFramework,
            LegacySignals: metadata.LegacySignals,
            DirectoryBuildPropsChain: metadata.DirectoryBuildPropsChain,
            DirectoryBuildTargetsChain: metadata.DirectoryBuildTargetsChain,
            CustomTargetsAndImports: metadata.CustomTargetsAndImports,
            Diagnostics: metadata.Diagnostics,
            EvaluationSucceeded: project.Evaluation.Succeeded,
            EvaluationErrorMessage: project.Evaluation.ErrorMessage,
            PackageReferences: packageReferences,
            Dependencies: dependencies,
            Dependents: dependents);
    }

    public static PackageSummaryDto ToSummary(PackageGraphNode node) => new(
        node.Package.PackageId,
        node.Package.HasVersionConflict,
        node.Package.ProjectVersions);

    public static UsageResultDto ToDto(UsageResult result) => new(
        result.TargetName,
        result.FilePath,
        result.LineNumber,
        result.MatchedSymbol,
        result.Kind,
        result.Confidence,
        result.ProjectPath,
        result.CodeSnippet);

    public static DependencyGraphDto ToDto(DependencyGraph graph)
    {
        var nodes = graph.Nodes.Select(n => n switch
        {
            ProjectGraphNode p => new GraphNodeDto(p.Id, "project", p.Project.Name, false, p.Project.TargetFrameworks),
            PackageGraphNode pkg => new GraphNodeDto(pkg.Id, "package", pkg.Package.PackageId, pkg.Package.HasVersionConflict, []),
            _ => new GraphNodeDto(n.Id, n.Kind.ToString().ToLowerInvariant(), n.Id, false, [])
        }).ToList();

        var edges = graph.Edges
            .Select(e => new GraphEdgeDto(e.Kind.ToString(), e.FromId, e.ToId, e.ResolvedPackageVersion))
            .ToList();

        return new DependencyGraphDto(nodes, edges);
    }
}
