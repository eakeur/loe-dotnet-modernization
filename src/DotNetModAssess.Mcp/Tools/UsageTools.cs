using System.ComponentModel;
using DotNetModAssess.Mcp.Dtos;
using DotNetModAssess.Mcp.Services;
using ModelContextProtocol.Server;

namespace DotNetModAssess.Mcp.Tools;

/// <summary>Tools for querying the eagerly-computed cross-solution usage results (project
/// namespaces and package ids actually referenced elsewhere in the solution's source).</summary>
[McpServerToolType]
public static class UsageTools
{
    [McpServerTool(Name = "find_usages", ReadOnly = true)]
    [Description(
        "Finds where a specific project (by its approximate root namespace/assembly name) or NuGet " +
        "package (by package id) is actually used elsewhere in the solution's source, with a " +
        "confidence tier per result: Confirmed (Roslyn syntax-tree match - using directive/type " +
        "reference/member access/attribute/base type), TextMatch (plain literal-name text search, " +
        "broader file coverage but higher false-positive rate), or SuffixMatch (speculative - " +
        "harvested from a shortened form of a Confirmed hit, catches bare-identifier usages but " +
        "noisiest, verify manually). Use this to answer \"is project/package X actually used, and " +
        "where, before I remove/upgrade/replace it\". The target name must match TargetName exactly " +
        "as it appears elsewhere (a project's RootNamespace/AssemblyName/Name, or an exact package " +
        "id) - use list_projects/list_packages/get_project_details to find the right value, or use " +
        "the search tool instead for free-text queries that aren't an exact known target.")]
    public static async Task<IReadOnlyList<UsageResultDto>> FindUsages(
        McpWorkspaceState state,
        [Description("The exact project namespace/assembly-name or package id to find usages of, matched against each UsageResult's TargetName.")]
        string target,
        CancellationToken cancellationToken = default)
    {
        await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);

        return (state.UsageResults ?? [])
            .Where(u => string.Equals(u.TargetName, target, StringComparison.Ordinal))
            .OrderBy(u => u.ProjectPath, StringComparer.Ordinal)
            .ThenBy(u => u.FilePath, StringComparer.Ordinal)
            .ThenBy(u => u.LineNumber)
            .Select(DtoMapping.ToDto)
            .ToList();
    }
}
