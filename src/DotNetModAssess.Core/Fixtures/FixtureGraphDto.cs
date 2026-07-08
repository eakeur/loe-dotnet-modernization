namespace DotNetModAssess.Core.Fixtures;

public sealed record FixturePackageVersionDto(string ProjectPath, string Version);

public sealed record FixtureGraphNodeDto(
    string Id,
    string Kind,
    IReadOnlyList<FixturePackageVersionDto>? ProjectVersions,
    bool? HasVersionConflict);

public sealed record FixtureGraphEdgeDto(string FromId, string ToId, string Kind, string? ResolvedPackageVersion);

public sealed record FixtureGraphDto(IReadOnlyList<FixtureGraphNodeDto> Nodes, IReadOnlyList<FixtureGraphEdgeDto> Edges);
