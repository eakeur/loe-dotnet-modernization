namespace DotNetModAssess.Core.Models;

public sealed class CustomBuildElement
{
    public required string ElementType { get; init; } // "Target" | "Import"
    public required string Name { get; init; }
    public string? DefiningFile { get; init; }
}
