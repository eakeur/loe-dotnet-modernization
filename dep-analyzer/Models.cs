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
    public int LinesOfCode { get; init; }
    public int LinesOfCode_cs { get; init; }
    public int LinesOfCode_vb { get; init; }
    public int LinesOfCode_csproj { get; init; }
    public int LinesOfCode_vbproj { get; init; }
    public int LinesOfCode_asmx { get; init; }
    public int LinesOfCode_resx { get; init; }
    public int LinesOfCode_json { get; init; }
    public int LinesOfCode_xml { get; init; }
    public int LinesOfCode_config { get; init; }
    public int LinesOfCode_aspx { get; init; }
    public int LinesOfCode_ascx { get; init; }
    public int LinesOfCode_razor { get; init; }
    public int LinesOfCode_cshtml { get; init; }
}

public record ProjectInfo(
    string Name,
    string AbsolutePath,
    bool IsSdkStyle,
    bool IsTestProject,
    string TargetFramework,
    List<PackageRef> Packages,
    List<string> ProjectRefPaths,
    int TotalLinesOfCode,
    Dictionary<string, int> LineCountsByExtension
);

public record PackageRef(string Id, string Version);
