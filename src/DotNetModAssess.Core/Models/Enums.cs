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

/// <summary>
/// How a <see cref="UsageResult"/> was found. <see cref="Confirmed"/> means Roslyn's syntax-tree
/// analysis matched a using directive/type reference/member access - low false-positive rate, but
/// misses bare (non-fully-qualified) identifier usages within .cs files and anything outside C#
/// source. <see cref="TextMatch"/> means only a plain word-boundary text search found it (any file
/// type) - higher recall, but higher false-positive rate (comments, string literals, coincidental
/// name collisions). Assessors should treat <see cref="TextMatch"/> results as "worth a manual
/// look", not as ground truth.
/// </summary>
public enum UsageConfidence
{
    Confirmed,
    TextMatch
}
