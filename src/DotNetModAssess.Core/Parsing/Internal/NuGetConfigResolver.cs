using DotNetModAssess.Core.Models;
using NuGet.Configuration;

namespace DotNetModAssess.Core.Parsing.Internal;

/// <summary>
/// Resolves the effective NuGet.Config hierarchy for a solution using NuGet.Configuration's own
/// standard fallback resolution (walking up from the given directory, merging user-level and
/// machine-wide settings per the normal NuGet precedence rules), rather than re-implementing
/// that precedence by hand.
/// </summary>
internal static class NuGetConfigResolver
{
    public static IReadOnlyList<NuGetSourceModel> ResolveSources(string solutionDirectory)
    {
        var settings = Settings.LoadDefaultSettings(solutionDirectory);
        var provider = new PackageSourceProvider(settings);

        return provider.LoadPackageSources()
            .Select(source => new NuGetSourceModel
            {
                Name = source.Name,
                Url = source.Source,
                IsEnabled = source.IsEnabled,
                ConfigFilePath = TryGetConfigPath(source),
            })
            .ToList();
    }

    private static string? TryGetConfigPath(PackageSource source)
    {
        try
        {
            return source.AsSourceItem().ConfigPath;
        }
        catch
        {
            return null;
        }
    }
}
