namespace DotNetModAssess.Core.Fixtures;

public sealed record FixtureEvaluationDiagnosticDto(
    string Severity,
    string Code,
    string Message,
    string? File,
    int? LineNumber);

public sealed record FixtureProjectMetadataDto(
    IReadOnlyDictionary<string, string?> RawProperties,
    string Format,
    string PackagesModel,
    bool IsCentrallyManaged,
    string TargetFrameworkRaw,
    string TargetFrameworkClassification,
    string OutputType,
    IReadOnlyList<string> ProjectTypeGuids,
    string? LangVersion,
    string? Nullable,
    bool? ImplicitUsings,
    bool? AllowUnsafeBlocks,
    bool? TreatWarningsAsErrors,
    IReadOnlyList<string> DefineConstants,
    string? AssemblyName,
    string? RootNamespace,
    IReadOnlyList<string> RuntimeIdentifiers,
    string? PlatformTarget,
    bool UseWindowsForms,
    bool UseWpf,
    bool? IsPackable,
    FixtureLegacyCouplingSignalsDto LegacySignals,
    IReadOnlyList<string> DirectoryBuildPropsChain,
    IReadOnlyList<string> DirectoryBuildTargetsChain,
    bool IsTestProject,
    string? TestFramework,
    IReadOnlyList<FixtureEvaluationDiagnosticDto>? Diagnostics = null);
