namespace DotNetModAssess.Core.Fixtures;

public sealed record FixturePackageReferenceDto(
    string PackageId,
    string? RequestedVersion,
    string? ResolvedVersion,
    bool IsCentrallyManaged,
    bool IsFloatingVersion);
