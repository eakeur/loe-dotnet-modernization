namespace DotNetModAssess.Core.Models;

public sealed class SolutionModel
{
    public required string Path { get; init; }
    public required IReadOnlyList<ProjectModel> Projects { get; init; }
    public IReadOnlyList<NuGetSourceModel> NuGetSources { get; init; } = [];
    public IReadOnlyList<string> DirectoryBuildPropsFiles { get; init; } = [];
    public IReadOnlyList<string> DirectoryBuildTargetsFiles { get; init; } = [];
    public string? DirectoryPackagesPropsPath { get; init; }

    public bool IsCentrallyManaged => DirectoryPackagesPropsPath is not null;
}
