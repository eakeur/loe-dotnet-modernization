using System.ComponentModel;
using DotNetModAssess.Mcp.Dtos;
using DotNetModAssess.Mcp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DotNetModAssess.Mcp.Tools;

/// <summary>Tools for querying the solution's dependency graph.</summary>
[McpServerToolType]
public static class GraphTools
{
    [McpServerTool(Name = "get_dependency_graph", ReadOnly = true)]
    [Description(
        "Returns the dependency subgraph rooted at a given project (by file path) or package (by " +
        "package id): the root node plus every node reachable from it in either direction (its full " +
        "transitive dependencies AND dependents), and only the edges among that node set - not the " +
        "whole solution's graph. Nodes carry Kind (\"project\" or \"package\"), Label, whether a " +
        "package node has a version conflict, and a project node's target frameworks. Edges carry " +
        "Kind (\"ProjectToProject\" or \"ProjectToPackage\"), FromId/ToId, and, for package edges, " +
        "the resolved version. Use this to understand what a specific project or package's removal/" +
        "upgrade would ripple into, without wading through the entire solution's graph.")]
    public static async Task<DependencyGraphDto> GetDependencyGraph(
        McpWorkspaceState state,
        [Description("The project's file path (as returned by list_projects) or package id (as returned by list_packages) to root the subgraph at.")]
        string rootId,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        var logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("DotNetModAssess.Mcp.Tools.GraphTools");
        logger.LogDebug("Tool invoked: get_dependency_graph (rootId={RootId})", rootId);
        await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);

        var graph = state.Graph
            ?? throw new McpException("No dependency graph is available for the loaded solution.");

        var root = graph.Nodes.FirstOrDefault(n => n.Id == rootId)
            ?? throw new McpException($"No project or package found with id '{rootId}'. Call list_projects or list_packages to see valid ids.");

        var subgraph = graph.GetRootedSubgraph(root);
        return DtoMapping.ToDto(subgraph);
    }
}
