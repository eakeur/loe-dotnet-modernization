namespace DotNetModAssess.Core.Models;

public sealed record PackageProjectVersion(string ProjectPath, string Version);

public sealed class PackageModel
{
    public required string PackageId { get; init; }
    public required IReadOnlyList<PackageProjectVersion> ProjectVersions { get; init; }
    public bool HasVersionConflict { get; init; }
}
