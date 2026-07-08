namespace DotNetModAssess.Core.Fixtures;

public sealed record FixtureSolutionDto(
    string Path,
    IReadOnlyList<FixtureProjectDto> Projects,
    IReadOnlyList<FixtureNuGetSourceDto> NuGetSources,
    IReadOnlyList<string> DirectoryBuildPropsFiles,
    IReadOnlyList<string> DirectoryBuildTargetsFiles,
    string? DirectoryPackagesPropsPath);
