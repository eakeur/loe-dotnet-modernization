namespace DotNetModAssess.Core.Fixtures;

public sealed record FixtureProjectDto(
    string Path,
    string Name,
    IReadOnlyList<string> TargetFrameworks,
    string Format,
    string OutputType,
    IReadOnlyList<string> ProjectReferencePaths,
    IReadOnlyList<FixturePackageReferenceDto> PackageReferences,
    FixtureEvaluationStatusDto Evaluation,
    IReadOnlyList<string> DirectoryBuildPropsChain,
    IReadOnlyList<string> DirectoryBuildTargetsChain,
    FixtureProjectMetadataDto Metadata);
