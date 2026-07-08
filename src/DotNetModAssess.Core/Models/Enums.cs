namespace DotNetModAssess.Core.Models;

public enum ProjectFormat
{
    Unknown = 0,
    SdkStyle,
    LegacyStyle
}

public enum PackagesModel
{
    Unknown = 0,
    PackageReference,
    PackagesConfig,
    None
}

public enum TargetFrameworkClassification
{
    Unknown = 0,
    NetFramework,
    NetStandard,
    NetCoreApp,
    Modern,
    Multi
}

public enum ProjectOutputType
{
    Unknown = 0,
    Library,
    Exe,
    WinExe,
    AppContainerExe
}

public enum EvaluationDiagnosticSeverity
{
    Warning,
    Error
}

public enum UsageReferenceKind
{
    UsingDirective,
    TypeReference,
    MemberAccess,
    Attribute,
    BaseTypeOrInterface,
    Other
}
