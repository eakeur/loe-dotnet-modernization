using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.PackageCompatibility;

/// <summary>
/// Checks a single package/version's compatibility with a target framework against the NuGet
/// sources configured for a solution (auto-discovered via NuGet.Config resolution, falling back to
/// nuget.org - mirroring how <c>NuGetConfigResolver</c> already resolves sources for the parsing
/// engine). Per-package/version, not batched - callers checking many packages should apply their
/// own bounded concurrency (e.g. a SemaphoreSlim) rather than this interface growing a batch method,
/// keeping this a single-responsibility client.
/// </summary>
public interface INuGetCompatibilityChecker
{
    Task<PackageCompatibilityInfo> CheckAsync(
        string solutionRootDirectory,
        string packageId,
        string currentVersion,
        string targetFrameworkMoniker,
        CancellationToken cancellationToken = default);
}
