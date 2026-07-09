using DotNetModAssess.Core.Models;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Core.Graph;

public sealed class DependencyGraph
{
    public required IReadOnlyList<GraphNode> Nodes { get; init; }
    public required IReadOnlyList<GraphEdge> Edges { get; init; }

    /// <summary>
    /// Nodes reachable via one outgoing edge from <paramref name="node"/> (what it depends on).
    /// </summary>
    public IReadOnlyList<GraphNode> GetDependencies(GraphNode node)
    {
        var nodesById = BuildNodeIndex();
        return Edges
            .Where(e => e.FromId == node.Id)
            .Select(e => nodesById[e.ToId])
            .ToList();
    }

    /// <summary>
    /// Nodes with one outgoing edge into <paramref name="node"/> (what depends on it).
    /// </summary>
    public IReadOnlyList<GraphNode> GetDependents(GraphNode node)
    {
        var nodesById = BuildNodeIndex();
        return Edges
            .Where(e => e.ToId == node.Id)
            .Select(e => nodesById[e.FromId])
            .ToList();
    }

    /// <summary>
    /// BFS over <see cref="GetDependencies"/> (Forward) or <see cref="GetDependents"/> (Reverse),
    /// returning every node transitively reachable from <paramref name="node"/>.
    /// The starting node itself is not included in the result.
    /// </summary>
    public IReadOnlyList<GraphNode> GetTransitiveClosure(GraphNode node, GraphDirection direction)
    {
        var nodesById = BuildNodeIndex();
        var edgesBySource = direction == GraphDirection.Forward
            ? Edges.ToLookup(e => e.FromId, e => e.ToId)
            : Edges.ToLookup(e => e.ToId, e => e.FromId);

        var visited = new HashSet<string>();
        var result = new List<GraphNode>();
        var queue = new Queue<string>();
        queue.Enqueue(node.Id);
        visited.Add(node.Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var neighborId in edgesBySource[currentId])
            {
                if (visited.Add(neighborId))
                {
                    result.Add(nodesById[neighborId]);
                    queue.Enqueue(neighborId);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a new <see cref="DependencyGraph"/> containing just <paramref name="root"/> plus
    /// every node reachable from it in either direction (its full transitive dependency <em>and</em>
    /// dependent closure), and only the edges among that node set. Used by the project/package
    /// detail panels so their graph tab renders a tree rooted at the thing being looked at rather
    /// than the entire solution's graph with everything else highlighted/dimmed - important once a
    /// solution has more than a couple hundred projects/packages (see the "known limit" warning in
    /// <c>DependencyGraphView</c>).
    /// </summary>
    public DependencyGraph GetRootedSubgraph(GraphNode root)
    {
        var forward = GetTransitiveClosure(root, GraphDirection.Forward);
        var reverse = GetTransitiveClosure(root, GraphDirection.Reverse);

        var nodeIds = new HashSet<string>(forward.Select(n => n.Id).Concat(reverse.Select(n => n.Id)))
        {
            root.Id
        };

        var nodes = Nodes.Where(n => nodeIds.Contains(n.Id)).ToList();
        var edges = Edges.Where(e => nodeIds.Contains(e.FromId) && nodeIds.Contains(e.ToId)).ToList();

        return new DependencyGraph { Nodes = nodes, Edges = edges };
    }

    private Dictionary<string, GraphNode> BuildNodeIndex() => Nodes.ToDictionary(n => n.Id);
}

public interface IDependencyGraphBuilder
{
    DependencyGraph Build(SolutionModel solution);
}

public sealed class NotImplementedDependencyGraphBuilder : IDependencyGraphBuilder
{
    public DependencyGraph Build(SolutionModel solution) => throw new NotImplementedException();
}

/// <summary>
/// Builds a <see cref="DependencyGraph"/> from a hydrated <see cref="SolutionModel"/>:
/// one <see cref="ProjectGraphNode"/> per project, one <see cref="PackageGraphNode"/> per
/// distinct package id (aggregating every project's resolved/requested version for it and
/// flagging a version conflict when more than one distinct version is used), plus
/// project-to-project and project-to-package edges mirroring the model's references.
/// </summary>
public sealed class DependencyGraphBuilder(ILogger<DependencyGraphBuilder>? logger = null) : IDependencyGraphBuilder
{
    public DependencyGraph Build(SolutionModel solution)
    {
        logger?.LogDebug("Building dependency graph for solution {SolutionPath} with {ProjectCount} projects", solution.Path, solution.Projects.Count);

        var projectNodes = solution.Projects
            .Select(p => new ProjectGraphNode { Project = p })
            .ToList();

        var packageVersionsById = new Dictionary<string, List<PackageProjectVersion>>();
        foreach (var project in solution.Projects)
        {
            foreach (var packageRef in project.PackageReferences)
            {
                var version = packageRef.ResolvedVersion ?? packageRef.RequestedVersion ?? string.Empty;
                if (!packageVersionsById.TryGetValue(packageRef.PackageId, out var versions))
                {
                    versions = [];
                    packageVersionsById[packageRef.PackageId] = versions;
                }

                versions.Add(new PackageProjectVersion(project.Path, version));
            }
        }

        var packageNodes = packageVersionsById
            .Select(kvp => new PackageGraphNode
            {
                Package = new PackageModel
                {
                    PackageId = kvp.Key,
                    ProjectVersions = kvp.Value,
                    HasVersionConflict = kvp.Value.Select(v => v.Version).Distinct().Count() > 1
                }
            })
            .ToList();

        var edges = new List<GraphEdge>();
        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.ProjectReferences)
            {
                edges.Add(new GraphEdge
                {
                    Kind = GraphEdgeKind.ProjectToProject,
                    FromId = project.Path,
                    ToId = reference.Path
                });
            }

            foreach (var packageRef in project.PackageReferences)
            {
                edges.Add(new GraphEdge
                {
                    Kind = GraphEdgeKind.ProjectToPackage,
                    FromId = project.Path,
                    ToId = packageRef.PackageId,
                    ResolvedPackageVersion = packageRef.ResolvedVersion ?? packageRef.RequestedVersion
                });
            }
        }

        var graph = new DependencyGraph
        {
            Nodes = [.. projectNodes.Cast<GraphNode>(), .. packageNodes],
            Edges = edges
        };

        logger?.LogInformation(
            "Built dependency graph with {NodeCount} nodes ({ProjectNodeCount} projects, {PackageNodeCount} packages) and {EdgeCount} edges",
            graph.Nodes.Count, projectNodes.Count, packageNodes.Count, edges.Count);

        return graph;
    }
}
