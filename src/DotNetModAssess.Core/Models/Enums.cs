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
/// How a <see cref="UsageResult"/> was found - three distinct evidence sources, not a graduated
/// precision scale:
/// <list type="bullet">
/// <item><see cref="Confirmed"/>: Roslyn's syntax-tree analysis matched a using directive/type
/// reference/member access - low false-positive rate, but misses bare (non-fully-qualified)
/// identifier usages within .cs files and anything outside C# source.</item>
/// <item><see cref="TextMatch"/>: a plain word-boundary text search for the target's own literal
/// name (namespace/package id) found it in a file Roslyn didn't (or doesn't) look at - higher
/// recall, but higher false-positive rate (comments, string literals, coincidental collisions).</item>
/// <item><see cref="SuffixMatch"/>: a word-boundary text search for a progressively-shortened
/// suffix harvested from a *different*, already-<see cref="Confirmed"/> qualified identifier (e.g.
/// a confirmed `System.Web.HttpContext` reference seeds searches for `Web.HttpContext` and bare
/// `HttpContext`) - this is what catches the classic "using System.Web; ... HttpContext.Current"
/// bare-identifier case neither of the other two tiers can. It is the weakest signal of the three:
/// short/common harvested suffixes (a type named `Client` or `Manager`) will produce real noise,
/// intentionally not suppressed by an arbitrary length cutoff - callers should present this tier
/// clearly labeled "speculative, verify manually" rather than trying to auto-filter it.</item>
/// </list>
/// </summary>
public enum UsageConfidence
{
    Confirmed,
    TextMatch,
    SuffixMatch
}
