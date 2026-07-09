using System.ComponentModel;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Mcp.Dtos;
using DotNetModAssess.Mcp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DotNetModAssess.Mcp.Tools;

/// <summary>Tools for listing and inspecting individual projects in the loaded solution.</summary>
[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(Name = "list_projects", ReadOnly = true)]
    [Description(
        "Lists every project in the loaded solution with its key metadata flags: file format (SDK-" +
        "style vs legacy-style), target framework, output type, packages model (PackageReference vs " +
        "packages.config), and legacy-coupling signals (System.Web/System.ServiceModel/System." +
        "Messaging references, ConfigurationManager usage, WinForms/WPF usage, COM references, " +
        "P/Invoke signals). Supports optional filters so you don't have to fetch every project and " +
        "filter client-side. Use this to survey the solution or narrow down to a specific category " +
        "of project (e.g. everything still on packages.config, or every legacy-style project) " +
        "before drilling into one with get_project_details.")]
    public static async Task<IReadOnlyList<ProjectSummaryDto>> ListProjects(
        McpWorkspaceState state,
        [Description("Only include projects with this format: \"SdkStyle\" or \"LegacyStyle\". Omit for all.")]
        string? format = null,
        [Description("Only include projects using this packages model: \"PackageReference\" or \"PackagesConfig\". Omit for all.")]
        string? packagesModel = null,
        [Description("Only include projects whose name or path contains this substring (case-insensitive). Omit for all.")]
        string? nameContains = null,
        [Description("If true, only include projects whose LegacyCouplingSignals.ReferencesSystemServiceModel is true (candidate WCF projects). If false, only include projects where it's false. Omit for either.")]
        bool? referencesSystemServiceModel = null,
        [Description("If true, only include projects that use System.Configuration.ConfigurationManager. If false, only include projects that don't. Omit for either.")]
        bool? usesConfigurationManager = null,
        [Description("If true, only include projects flagged as test projects. If false, only include non-test projects. Omit for either.")]
        bool? isTestProject = null,
        CancellationToken cancellationToken = default)
    {
        var solution = await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);

        IEnumerable<ProjectModel> query = solution.Projects;

        if (!string.IsNullOrWhiteSpace(format) && Enum.TryParse<ProjectFormat>(format, ignoreCase: true, out var parsedFormat))
        {
            query = query.Where(p => p.Format == parsedFormat);
        }

        if (!string.IsNullOrWhiteSpace(packagesModel) && Enum.TryParse<PackagesModel>(packagesModel, ignoreCase: true, out var parsedPackagesModel))
        {
            query = query.Where(p => p.Metadata.PackagesModel == parsedPackagesModel);
        }

        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            query = query.Where(p =>
                p.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
        }

        if (referencesSystemServiceModel.HasValue)
        {
            query = query.Where(p => p.Metadata.LegacySignals.ReferencesSystemServiceModel == referencesSystemServiceModel.Value);
        }

        if (usesConfigurationManager.HasValue)
        {
            query = query.Where(p => p.Metadata.LegacySignals.UsesConfigurationManager == usesConfigurationManager.Value);
        }

        if (isTestProject.HasValue)
        {
            query = query.Where(p => p.Metadata.IsTestProject == isTestProject.Value);
        }

        return query
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(DtoMapping.ToSummary)
            .ToList();
    }

    [McpServerTool(Name = "get_project_details", ReadOnly = true)]
    [Description(
        "Full metadata for a single project - target framework(s), langversion/nullable/output " +
        "settings, legacy coupling signals, Directory.Build.* chain, custom MSBuild targets/" +
        "imports, evaluation diagnostics, package references (with per-package conflict flag), and " +
        "its dependency-graph neighbors: which projects it directly references (Dependencies) and " +
        "which projects directly reference it (Dependents). Use list_projects first to find the " +
        "exact project path to pass here.")]
    public static async Task<ProjectDetailsDto> GetProjectDetails(
        McpWorkspaceState state,
        [Description("The project's file path, exactly as returned by list_projects' Path field (e.g. \"src/Foo/Foo.csproj\").")]
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var solution = await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);

        var project = solution.Projects.FirstOrDefault(p => p.Path == projectPath)
            ?? throw new McpException($"No project found with path '{projectPath}'. Call list_projects to see valid paths.");

        return DtoMapping.ToDetails(project, state.Graph, solution);
    }
}
