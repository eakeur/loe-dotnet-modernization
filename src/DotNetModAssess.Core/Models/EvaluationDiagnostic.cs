namespace DotNetModAssess.Core.Models;

public sealed class EvaluationDiagnostic
{
    public required EvaluationDiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? File { get; init; }
    public int? LineNumber { get; init; }
}
