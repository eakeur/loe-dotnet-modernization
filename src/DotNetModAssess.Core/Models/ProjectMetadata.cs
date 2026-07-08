namespace DotNetModAssess.Core.Models;

public sealed class ProjectMetadata
{
    public required IReadOnlyDictionary<string, string?> RawProperties { get; init; }

    public ProjectFormat Format { get; init; }
    public PackagesModel PackagesModel { get; init; }
    public bool IsCentrallyManaged { get; init; }

    public required string TargetFrameworkRaw { get; init; }
    public TargetFrameworkClassification TargetFrameworkClassification { get; init; }

    public ProjectOutputType OutputType { get; init; }
    public IReadOnlyList<Guid> ProjectTypeGuids { get; init; } = [];

    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }
    public bool? ImplicitUsings { get; init; }
    public bool? AllowUnsafeBlocks { get; init; }
    public bool? TreatWarningsAsErrors { get; init; }
    public IReadOnlyList<string> DefineConstants { get; init; } = [];

    public string? AssemblyName { get; init; }
    public string? RootNamespace { get; init; }
    public IReadOnlyList<string> RuntimeIdentifiers { get; init; } = [];
    public string? PlatformTarget { get; init; }
    public bool UseWindowsForms { get; init; }
    public bool UseWpf { get; init; }
    public bool? IsPackable { get; init; }

    public LegacyCouplingSignals LegacySignals { get; init; } = new();

    public IReadOnlyList<string> DirectoryBuildPropsChain { get; init; } = [];
    public IReadOnlyList<string> DirectoryBuildTargetsChain { get; init; } = [];
    public IReadOnlyList<CustomBuildElement> CustomTargetsAndImports { get; init; } = [];

    public bool IsTestProject { get; init; }
    public string? TestFramework { get; init; }

    public IReadOnlyList<EvaluationDiagnostic> Diagnostics { get; init; } = [];
}
