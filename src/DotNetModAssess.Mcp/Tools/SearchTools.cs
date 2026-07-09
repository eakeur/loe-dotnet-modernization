using System.ComponentModel;
using DotNetModAssess.Core.Search;
using DotNetModAssess.Mcp.Dtos;
using DotNetModAssess.Mcp.Services;
using ModelContextProtocol.Server;

namespace DotNetModAssess.Mcp.Tools;

/// <summary>Ad-hoc free-text search over the loaded solution's source, for queries that aren't one
/// of the solution's known projects/packages/legacy-patterns.</summary>
[McpServerToolType]
public static class SearchTools
{
    [McpServerTool(Name = "search", ReadOnly = true)]
    [Description(
        "Runs an arbitrary free-text search across the loaded solution's source - NOT limited to " +
        "known project namespaces, package ids, or the five built-in legacy patterns (use " +
        "find_usages or get_legacy_findings for those, which are more precise for their specific " +
        "targets). Use this for exploratory questions like \"does anything reference " +
        "'HttpContext.Current'\", \"where is this connection string used\", or any other literal " +
        "string/identifier an agent wants to grep for across the whole solution.")]
    public static async Task<IReadOnlyList<UsageResultDto>> Search(
        McpWorkspaceState state,
        IAdHocSourceSearcher searcher,
        [Description("The free-text query to search for across the solution's source.")]
        string query,
        CancellationToken cancellationToken = default)
    {
        var solution = await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);

        var results = await searcher.SearchAsync(solution, query, cancellationToken);

        return results
            .OrderBy(u => u.ProjectPath, StringComparer.Ordinal)
            .ThenBy(u => u.FilePath, StringComparer.Ordinal)
            .ThenBy(u => u.LineNumber)
            .Select(DtoMapping.ToDto)
            .ToList();
    }
}
