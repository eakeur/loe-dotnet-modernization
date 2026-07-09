namespace DotNetModAssess.Core.Models;

public sealed class UsageResult
{
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }
    public required string MatchedSymbol { get; init; }
    public required UsageReferenceKind Kind { get; init; }
    public string? ProjectPath { get; init; }
    public string? CodeSnippet { get; init; }

    /// <summary>Defaults to <see cref="UsageConfidence.Confirmed"/> so existing callers/fixtures
    /// that only ever produced Roslyn-confirmed results don't need to change.</summary>
    public UsageConfidence Confidence { get; init; } = UsageConfidence.Confirmed;

    /// <summary>
    /// The scanning target (a project's approximate namespace, or a package id) this result is
    /// attributed to. Not always equal to (or a prefix-match of) <see cref="MatchedSymbol"/>: a
    /// Confirmed/SuffixMatch result's MatchedSymbol is often a longer qualified form actually found
    /// in the source (e.g. "System.Web.HttpContext"), while a TextMatch result's MatchedSymbol is
    /// the bare target name itself (e.g. "System.Web") - both should carry
    /// <c>TargetName == "System.Web"</c> so callers can group/deduplicate by "what is this a usage
    /// of" without re-deriving it from MatchedSymbol. Defaults to empty for any pre-existing
    /// caller/fixture that never set it (there is no meaningful target to infer for those).
    /// </summary>
    public string TargetName { get; init; } = string.Empty;
}
