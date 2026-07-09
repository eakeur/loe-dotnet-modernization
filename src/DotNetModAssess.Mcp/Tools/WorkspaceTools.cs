using System.ComponentModel;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Mcp.Dtos;
using DotNetModAssess.Mcp.Services;
using ModelContextProtocol.Server;

namespace DotNetModAssess.Mcp.Tools;

/// <summary>Tools for checking load progress, getting a solution-wide summary, and manually
/// re-triggering a load.</summary>
[McpServerToolType]
public static class WorkspaceTools
{
    [McpServerTool(Name = "get_load_status", ReadOnly = true)]
    [Description(
        "Reports whether the configured solution is still loading, finished loading, or failed, " +
        "plus the current progress stage message and (once loaded) basic counts (project count, " +
        "usage-result count, legacy-finding count). Returns IMMEDIATELY, never waits for loading " +
        "to finish - call this first / poll it if you want to watch progress on a large solution " +
        "instead of blocking on a data tool. Every other tool in this server already waits " +
        "internally for loading to finish before returning, so you do not need to poll this before " +
        "calling them - it's for visibility only.")]
    public static LoadStatusSnapshot GetLoadStatus(McpWorkspaceState state) => state.GetLoadStatusSnapshot();

    [McpServerTool(Name = "get_solution_overview", ReadOnly = true)]
    [Description(
        "High-level summary of the whole loaded solution: total project count, SDK-style vs " +
        "legacy-style project counts, how many projects are still on packages.config, how many " +
        "NuGet sources are configured, distinct package count and how many have a version conflict " +
        "across projects, the target-framework breakdown, and a count of legacy migration-blocker " +
        "findings (WCF/WPF/ConfigurationManager/AppDomain/COM interop) grouped by pattern. Use this " +
        "as the first call when starting an assessment, to get oriented before drilling into " +
        "individual projects/packages/findings.")]
    public static async Task<SolutionOverviewDto> GetSolutionOverview(McpWorkspaceState state, CancellationToken cancellationToken = default)
    {
        var solution = await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);
        var graph = state.Graph;

        var projects = solution.Projects;
        var tfmBreakdown = projects
            .Select(p => string.IsNullOrWhiteSpace(p.Metadata.TargetFrameworkRaw) ? "(unknown)" : p.Metadata.TargetFrameworkRaw)
            .GroupBy(tfm => tfm)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new TfmCountDto(g.Key, g.Count()))
            .ToList();

        var findingsByPattern = (state.LegacyFindings ?? [])
            .GroupBy(f => f.TargetName)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PatternCountDto(g.Key, g.Count()))
            .ToList();

        var packageNodes = (graph?.Nodes.OfType<PackageGraphNode>() ?? []).ToList();

        return new SolutionOverviewDto(
            SolutionPath: solution.Path,
            ProjectCount: projects.Count,
            SdkStyleProjectCount: projects.Count(p => p.Format == ProjectFormat.SdkStyle),
            LegacyStyleProjectCount: projects.Count(p => p.Format == ProjectFormat.LegacyStyle),
            PackagesConfigProjectCount: projects.Count(p => p.Metadata.PackagesModel == PackagesModel.PackagesConfig),
            CentrallyManagedProjectCount: projects.Count(p => p.Metadata.IsCentrallyManaged),
            NuGetSourceCount: solution.NuGetSources.Count,
            DistinctPackageCount: packageNodes.Count,
            PackageVersionConflictCount: packageNodes.Count(p => p.Package.HasVersionConflict),
            IsWatching: state.IsWatching,
            TargetFrameworkBreakdown: tfmBreakdown,
            LegacyFindingsByPattern: findingsByPattern);
    }

    [McpServerTool(Name = "reload", Destructive = false)]
    [Description(
        "Manually re-triggers a full re-parse/re-scan of the configured solution, bypassing the " +
        "automatic debounced file-watcher reload (which already re-parses on any .sln/.slnf/" +
        ".csproj/Directory.Build.*/Directory.Packages.props/NuGet.Config change under the solution " +
        "directory). Use this as a fast-path fallback if you know files changed but want to be sure " +
        "the loaded data is fresh right now, rather than waiting for the watcher's debounce. Waits " +
        "for the reload to finish before returning, then reports the same status get_load_status " +
        "would.")]
    public static async Task<LoadStatusSnapshot> Reload(McpWorkspaceState state, CancellationToken cancellationToken = default)
    {
        await state.ReloadAsync(cancellationToken);
        return state.GetLoadStatusSnapshot();
    }
}
