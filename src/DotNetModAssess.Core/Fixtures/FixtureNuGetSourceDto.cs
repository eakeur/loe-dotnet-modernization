namespace DotNetModAssess.Core.Fixtures;

public sealed record FixtureNuGetSourceDto(string Name, string Url, bool IsEnabled, string? ConfigFilePath);
