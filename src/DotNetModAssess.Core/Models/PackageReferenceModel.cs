namespace DotNetModAssess.Core.Models;

public sealed class PackageReferenceModel
{
    public required string PackageId { get; init; }
    public string? RequestedVersion { get; init; }
    public string? ResolvedVersion { get; init; }
    public bool IsCentrallyManaged { get; init; }
    public bool IsFloatingVersion { get; init; }
}
