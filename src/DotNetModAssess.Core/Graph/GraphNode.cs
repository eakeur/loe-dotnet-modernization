namespace DotNetModAssess.Core.Graph;

public enum GraphNodeKind
{
    Project,
    Package
}

public abstract class GraphNode
{
    public abstract GraphNodeKind Kind { get; }
    public abstract string Id { get; }
}
