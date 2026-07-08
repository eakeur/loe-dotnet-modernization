namespace DotNetModAssess.Core.Models;

public sealed class ProjectEvaluationStatus
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static ProjectEvaluationStatus Ok() => new() { Succeeded = true };

    public static ProjectEvaluationStatus Failed(string message) =>
        new() { Succeeded = false, ErrorMessage = message };
}
