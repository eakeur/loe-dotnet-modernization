namespace DotNetModAssess.Core.Graph;

public sealed class GraphEdge
{
    public required GraphEdgeKind Kind { get; init; }
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public string? ResolvedPackageVersion { get; init; }
}
