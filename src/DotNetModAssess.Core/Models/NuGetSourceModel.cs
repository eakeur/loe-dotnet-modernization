namespace DotNetModAssess.Core.Models;

public sealed class NuGetSourceModel
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string? ConfigFilePath { get; init; }
}
