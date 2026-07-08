using Microsoft.Build.Locator;

namespace DotNetModAssess.Core.Parsing.Internal;

/// <summary>
/// Ensures MSBuildLocator has registered an MSBuild instance for the current process before any
/// Buildalyzer/MSBuild API is touched. Buildalyzer's own MSBuild assemblies are referenced with
/// ExcludeAssets="runtime" in the project file specifically so that this registration (which
/// redirects assembly loads to the MSBuild that ships with the installed .NET SDK) is what
/// actually gets used at runtime, avoiding version-mismatch load failures.
/// </summary>
internal static class MSBuildEnvironmentInitializer
{
    private static readonly object Lock = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered || MSBuildLocator.IsRegistered)
        {
            _registered = true;
            return;
        }

        lock (Lock)
        {
            if (_registered || MSBuildLocator.IsRegistered)
            {
                _registered = true;
                return;
            }

            MSBuildLocator.RegisterDefaults();
            _registered = true;
        }
    }
}
