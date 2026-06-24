using System.Text.Json.Serialization;

namespace DepTree;

public record ProjectInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string ProjectStyle { get; init; } = "";
    public string OutputType { get; init; } = "";
    public string AssemblyName { get; init; } = "";
    public string RootNamespace { get; init; } = "";
    public List<string> TargetFrameworks { get; init; } = [];
    public string FrameworkClass { get; init; } = "";
    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }
    public string? ImplicitUsings { get; init; }
    public bool IsTestProject { get; init; }
    public bool HasDockerfile { get; init; }
    public ConfigFiles ConfigFiles { get; init; } = new();
    public int CsFileCount { get; init; }
    public List<PackageRef> Packages { get; init; } = [];
    public List<string> ProjectRefs { get; set; } = [];
    public List<string> Dependents { get; set; } = [];
    public MigrationRisk MigrationRisk { get; init; } = new();
}

public record ConfigFiles
{
    public bool AppSettings { get; init; }
    public bool WebConfig { get; init; }
    public bool AppConfig { get; init; }
}

public record PackageRef
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public bool? SupportsNet8 { get; init; }
    public bool? SupportsNet10 { get; init; }
}

public record MigrationRisk
{
    public string Level { get; init; } = "Low";
    public List<string> Issues { get; init; } = [];
}

public record SolutionSummary
{
    public string GeneratedAt { get; init; } = "";
    public string SolutionPath { get; init; } = "";
    public int TotalProjects { get; init; }
    public int TestProjects { get; init; }
    public RiskCount RiskSummary { get; init; } = new();
    public List<FrameworkGroup> FrameworkSummary { get; init; } = [];
    public List<TopPackage> TopPackages { get; init; } = [];
}

public record RiskCount
{
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
}

public record FrameworkGroup
{
    [JsonPropertyName("class")]
    public string Class { get; init; } = "";
    public int Count { get; init; }
    public List<string> Tfms { get; init; } = [];
}

public record TopPackage
{
    public string Name { get; init; } = "";
    public int UsedBy { get; init; }
    public List<string> Versions { get; init; } = [];
    public bool? SupportsNet8 { get; init; }
    public bool? SupportsNet10 { get; init; }
}

public record SolutionOutput
{
    public SolutionSummary Summary { get; init; } = new();
    public List<ProjectInfo> Projects { get; init; } = [];
}
