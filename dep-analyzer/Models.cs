namespace DepAnalyzer;

public record DependencyRow
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Version { get; init; } = "";
    public string TargetFramework { get; init; } = "";
    public bool? SupportsNet8 { get; set; }
    public string PackageFrameworks { get; set; } = "";
    public int InternalProjectDependencies { get; init; }
    public bool IsTestProject { get; init; }
    public string ProjectFormat { get; init; } = "";
    public int Level { get; set; }
}

public record ProjectInfo(
    string Name,
    string AbsolutePath,
    bool IsSdkStyle,
    bool IsTestProject,
    string TargetFramework,
    List<PackageRef> Packages,
    List<string> ProjectRefPaths
);

public record PackageRef(string Id, string Version);
