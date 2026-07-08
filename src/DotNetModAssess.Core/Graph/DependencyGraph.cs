using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Graph;

public sealed class DependencyGraph
{
    public required IReadOnlyList<GraphNode> Nodes { get; init; }
    public required IReadOnlyList<GraphEdge> Edges { get; init; }

    public IReadOnlyList<GraphNode> GetDependencies(GraphNode node) => throw new NotImplementedException();

    public IReadOnlyList<GraphNode> GetDependents(GraphNode node) => throw new NotImplementedException();

    public IReadOnlyList<GraphNode> GetTransitiveClosure(GraphNode node, GraphDirection direction) =>
        throw new NotImplementedException();
}

public interface IDependencyGraphBuilder
{
    DependencyGraph Build(SolutionModel solution);
}

public sealed class NotImplementedDependencyGraphBuilder : IDependencyGraphBuilder
{
    public DependencyGraph Build(SolutionModel solution) => throw new NotImplementedException();
}
