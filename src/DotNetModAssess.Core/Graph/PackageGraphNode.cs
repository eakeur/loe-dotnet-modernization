using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Graph;

public sealed class PackageGraphNode : GraphNode
{
    public override GraphNodeKind Kind => GraphNodeKind.Package;
    public override string Id => Package.PackageId;
    public required PackageModel Package { get; init; }
}
