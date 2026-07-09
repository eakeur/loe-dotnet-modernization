using System.ComponentModel;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.PackageCompatibility;
using DotNetModAssess.Mcp.Dtos;
using DotNetModAssess.Mcp.Services;
using ModelContextProtocol.Server;

namespace DotNetModAssess.Mcp.Tools;

/// <summary>Tools for listing packages used across the solution and checking a specific
/// package/version's compatibility with a target framework.</summary>
[McpServerToolType]
public static class PackageTools
{
    [McpServerTool(Name = "list_packages", ReadOnly = true)]
    [Description(
        "Lists every distinct NuGet package referenced anywhere in the loaded solution, with which " +
        "project(s) reference it and at which version(s), and whether it has a version conflict " +
        "(different projects requesting/resolving different versions of the same package - a " +
        "common source of build/runtime surprises worth flagging in an assessment). Does NOT check " +
        "NuGet.org compatibility with a target framework - use check_package_compatibility for that, " +
        "per package, on demand.")]
    public static async Task<IReadOnlyList<PackageSummaryDto>> ListPackages(McpWorkspaceState state, CancellationToken cancellationToken = default)
    {
        await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);

        var graph = state.Graph;
        if (graph is null)
        {
            return [];
        }

        return graph.Nodes.OfType<PackageGraphNode>()
            .OrderBy(n => n.Package.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(DtoMapping.ToSummary)
            .ToList();
    }

    [McpServerTool(Name = "check_package_compatibility", ReadOnly = true)]
    [Description(
        "Checks, via a live NuGet query against whatever sources are configured for this solution " +
        "(private feeds honored, falling back to nuget.org), whether a specific package/version is " +
        "compatible with a target framework, and if not, the lowest newer published version that " +
        "is. This is a live network call per invocation (not precomputed/cached at load time), so " +
        "call it on-demand for packages you actually care about rather than in a loop over every " +
        "package in the solution - use list_packages first to decide which ones matter (e.g. ones " +
        "with old-looking versions or version conflicts).")]
    public static async Task<PackageCompatibilityInfo> CheckPackageCompatibility(
        McpWorkspaceState state,
        INuGetCompatibilityChecker checker,
        [Description("The NuGet package id, e.g. \"Newtonsoft.Json\".")]
        string packageId,
        [Description("The version currently in use, e.g. \"9.0.1\".")]
        string currentVersion,
        [Description("The target framework moniker to check compatibility against, e.g. \"net8.0\". Defaults to \"net8.0\" if omitted.")]
        string? targetFrameworkMoniker = "net8.0",
        CancellationToken cancellationToken = default)
    {
        var solution = await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);
        var solutionRoot = Path.GetDirectoryName(solution.Path) ?? string.Empty;

        return await checker.CheckAsync(
            solutionRoot,
            packageId,
            currentVersion,
            string.IsNullOrWhiteSpace(targetFrameworkMoniker) ? "net8.0" : targetFrameworkMoniker,
            cancellationToken);
    }
}
