namespace DotNetModAssess.Core.Models;

public sealed class ProjectModel
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> TargetFrameworks { get; init; }
    public ProjectFormat Format { get; init; }
    public ProjectOutputType OutputType { get; init; }
    public IReadOnlyList<ProjectModel> ProjectReferences { get; init; } = [];
    public IReadOnlyList<PackageReferenceModel> PackageReferences { get; init; } = [];
    public ProjectEvaluationStatus Evaluation { get; init; } = ProjectEvaluationStatus.Ok();
    public IReadOnlyList<string> DirectoryBuildPropsChain { get; init; } = [];
    public IReadOnlyList<string> DirectoryBuildTargetsChain { get; init; } = [];
    public required ProjectMetadata Metadata { get; init; }
}
