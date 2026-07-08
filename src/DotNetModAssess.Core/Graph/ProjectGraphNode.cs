using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Graph;

public sealed class ProjectGraphNode : GraphNode
{
    public override GraphNodeKind Kind => GraphNodeKind.Project;
    public override string Id => Project.Path;
    public required ProjectModel Project { get; init; }
}
