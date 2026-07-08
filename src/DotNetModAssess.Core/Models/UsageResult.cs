namespace DotNetModAssess.Core.Models;

public sealed class UsageResult
{
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }
    public required string MatchedSymbol { get; init; }
    public required UsageReferenceKind Kind { get; init; }
    public string? ProjectPath { get; init; }
    public string? CodeSnippet { get; init; }
}
