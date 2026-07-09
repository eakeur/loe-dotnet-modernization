using System.Collections.Concurrent;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing.Internal;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DotNetModAssess.Core.PackageCompatibility;

/// <summary>
/// Real <see cref="INuGetCompatibilityChecker"/> implementation built on the official
/// NuGet.Protocol client library (not a raw HTTP call to nuget.org's REST API), so it respects
/// whatever NuGet sources are configured for the solution (private feeds, auth via the ambient
/// NuGet.Config, etc.) - the same discovery <see cref="NuGetConfigResolver"/> already performs for
/// the parsing engine, reused here rather than re-implemented.
///
/// For a given package/version/target framework: resolves the configured sources, finds the first
/// one that knows about the package (graceful per-source degradation - a private feed being down
/// doesn't fail the check if nuget.org also has the package), fetches every published version via
/// <see cref="FindPackageByIdResource.GetAllVersionsAsync"/>, and determines compatibility of a
/// given version via <see cref="FindPackageByIdResource.GetDependencyInfoAsync"/> + <see
/// cref="DefaultCompatibilityProvider"/> against that version's dependency groups. See
/// <see cref="NuGetVersionCompatibilityEvaluator"/> for the canonical (and directly unit-tested)
/// description of the "current compatible? if not, what's the first newer compatible version?"
/// algorithm this mirrors asynchronously below.
///
/// Registered as a DI singleton (see Program.cs): this class holds no per-circuit/per-request
/// mutable state of its own - the only state is <see cref="ResultCache"/>, which is deliberately
/// process-lifetime (see its own doc comment), so one shared instance is simpler than a new one
/// per Blazor circuit for no benefit.
/// </summary>
public sealed class NuGetCompatibilityChecker : INuGetCompatibilityChecker
{
    private static readonly string[] FallbackSources = ["https://api.nuget.org/v3/index.json"];

    /// <summary>
    /// Process-lifetime cache of successful checks, keyed by (packageId, currentVersion, tfm) -
    /// mirrors the <c>EvaluationCache</c> pattern (see Parsing/Internal/EvaluationCache.cs) of a
    /// static <see cref="ConcurrentDictionary{TKey,TValue}"/> shared across every circuit/request,
    /// so repeated page loads/"Check all" runs for the same package/version don't repeatedly hit
    /// the network. Unlike EvaluationCache there is no local file-write-time (or any other
    /// server-side signal) to key invalidation on - published NuGet package metadata essentially
    /// never changes retroactively for an already-published version, so a plain cache with no
    /// expiry/TTL is an acceptable v1 simplification rather than something worth building
    /// infrastructure for; a *failed* check is deliberately never cached (see <see cref="CheckAsync"/>)
    /// so a transient network hiccup doesn't stick around for the rest of the process's lifetime
    /// the way a genuine "not compatible" result should.
    /// </summary>
    private static readonly ConcurrentDictionary<string, PackageCompatibilityInfo> ResultCache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<PackageCompatibilityInfo> CheckAsync(
        string solutionRootDirectory,
        string packageId,
        string currentVersion,
        string targetFrameworkMoniker,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(packageId, currentVersion, targetFrameworkMoniker);
        if (ResultCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        PackageCompatibilityInfo result;
        try
        {
            result = await CheckUncachedAsync(solutionRootDirectory, packageId, currentVersion, targetFrameworkMoniker, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller-initiated cancellation (e.g. navigating away mid-check) is not a "check
            // failure" to swallow - let it propagate so callers (e.g. a bounded-concurrency
            // "check all" loop) see it the same way any other cancelled Task would behave.
            throw;
        }
        catch (Exception ex)
        {
            // Final safety net: every failure mode we know about is already caught and converted
            // to a Failed(...) result inside CheckUncachedAsync, but this guarantees the "never
            // throw out of CheckAsync" contract holds even for a failure mode we didn't think of
            // (e.g. a malformed source URL from an unusual NuGet.Config).
            result = Failed(packageId, currentVersion, targetFrameworkMoniker, $"Unexpected error: {ex.Message}");
        }

        if (result.CheckSucceeded)
        {
            ResultCache[cacheKey] = result;
        }

        return result;
    }

    private static async Task<PackageCompatibilityInfo> CheckUncachedAsync(
        string solutionRootDirectory,
        string packageId,
        string currentVersion,
        string targetFrameworkMoniker,
        CancellationToken cancellationToken)
    {
        NuGetFramework targetFramework;
        try
        {
            targetFramework = NuGetFramework.Parse(targetFrameworkMoniker);
        }
        catch (Exception ex)
        {
            return Failed(packageId, currentVersion, targetFrameworkMoniker,
                $"Could not parse target framework moniker '{targetFrameworkMoniker}': {ex.Message}");
        }

        if (targetFramework.IsUnsupported)
        {
            return Failed(packageId, currentVersion, targetFrameworkMoniker,
                $"Target framework moniker '{targetFrameworkMoniker}' is not a recognized framework.");
        }

        if (!NuGetVersion.TryParse(currentVersion, out var parsedCurrentVersion) || parsedCurrentVersion is null)
        {
            return Failed(packageId, currentVersion, targetFrameworkMoniker,
                $"Current version '{currentVersion}' is not a valid NuGet version string.");
        }

        var repositories = BuildRepositories(solutionRootDirectory);
        using var cacheContext = new SourceCacheContext();
        var logger = NullLogger.Instance;

        FindPackageByIdResource? workingResource = null;
        List<NuGetVersion>? allVersions = null;
        var sourceErrors = new List<string>();

        foreach (var repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
                if (resource is null)
                {
                    continue;
                }

                var versions = (await resource.GetAllVersionsAsync(packageId, cacheContext, logger, cancellationToken).ConfigureAwait(false))
                    ?.ToList();

                if (versions is { Count: > 0 })
                {
                    workingResource = resource;
                    allVersions = versions;
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Graceful per-source failure handling, mirroring the old dep-analyzer prototype:
                // one misconfigured/unreachable source (e.g. a private feed requiring auth we
                // don't have) shouldn't fail the whole check if another configured source (often
                // nuget.org itself) has the package.
                sourceErrors.Add($"{repository.PackageSource.Name}: {ex.Message}");
            }
        }

        if (workingResource is null || allVersions is null)
        {
            var detail = sourceErrors.Count > 0 ? $" Errors: {string.Join("; ", sourceErrors)}" : string.Empty;
            return Failed(packageId, currentVersion, targetFrameworkMoniker,
                $"Package '{packageId}' was not found on any of the {repositories.Count} configured source(s).{detail}");
        }

        var latestPublishedVersion = allVersions.Count == 0 ? null : allVersions.Max()!.ToNormalizedString();

        async Task<bool> IsCompatibleAsync(NuGetVersion version)
        {
            var dependencyInfo = await workingResource.GetDependencyInfoAsync(packageId, version, cacheContext, logger, cancellationToken)
                .ConfigureAwait(false);
            return dependencyInfo is not null && HasCompatibleDependencyGroup(dependencyInfo.DependencyGroups, targetFramework);
        }

        bool currentVersionIsCompatible;
        try
        {
            currentVersionIsCompatible = await IsCompatibleAsync(parsedCurrentVersion).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(packageId, currentVersion, targetFrameworkMoniker,
                $"Could not retrieve dependency info for {packageId} {currentVersion}: {ex.Message}");
        }

        // Mirrors NuGetVersionCompatibilityEvaluator.Evaluate's algorithm (ascending scan,
        // short-circuit on first compatible newer version) - see that class's doc comment for why
        // this can't just call it directly (the per-version compatibility check here is async).
        string? recommendedUpgradeVersion = null;
        if (!currentVersionIsCompatible)
        {
            foreach (var candidate in allVersions.Where(v => v > parsedCurrentVersion).OrderBy(v => v))
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool compatible;
                try
                {
                    compatible = await IsCompatibleAsync(candidate).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // A single version's dependency info failing to resolve shouldn't abort the
                    // search for a recommended upgrade - just skip it and keep looking.
                    continue;
                }

                if (compatible)
                {
                    recommendedUpgradeVersion = candidate.ToNormalizedString();
                    break;
                }
            }
        }

        return new PackageCompatibilityInfo
        {
            PackageId = packageId,
            CurrentVersion = currentVersion,
            TargetFrameworkMoniker = targetFrameworkMoniker,
            CurrentVersionIsCompatible = currentVersionIsCompatible,
            RecommendedUpgradeVersion = recommendedUpgradeVersion,
            LatestPublishedVersion = latestPublishedVersion,
            CheckSucceeded = true,
            ErrorMessage = null,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Resolves the effective NuGet source list for <paramref name="solutionRootDirectory"/> via
    /// <see cref="NuGetConfigResolver"/> (same NuGet.Config-hierarchy walking the real solution
    /// parser already uses), falling back to nuget.org alone if resolution yields nothing enabled
    /// - mirroring the old dep-analyzer prototype's <c>BuildRepositories</c> fallback behavior.
    /// </summary>
    private static IReadOnlyList<SourceRepository> BuildRepositories(string solutionRootDirectory)
    {
        IReadOnlyList<NuGetSourceModel> sources;
        try
        {
            sources = NuGetConfigResolver.ResolveSources(solutionRootDirectory).Where(s => s.IsEnabled).ToList();
        }
        catch
        {
            sources = [];
        }

        var urls = sources.Count > 0 ? sources.Select(s => s.Url) : FallbackSources;
        return urls.Select(Repository.Factory.GetCoreV3).ToList();
    }

    /// <summary>
    /// Whether any of a version's dependency groups are compatible with <paramref name="targetFramework"/>,
    /// via <see cref="DefaultCompatibilityProvider"/> - the same core logic the old dep-analyzer
    /// prototype used, which also treats netstandard2.x-and-below packages as compatible with
    /// net8.0 for free, via NuGet's own compatibility rules (no separate "or netstandard" case
    /// needed). A version with zero declared dependency groups (no framework-specific dependencies
    /// at all) is treated as compatible - there's nothing about its own declared dependencies that
    /// would rule out <paramref name="targetFramework"/>.
    /// </summary>
    private static bool HasCompatibleDependencyGroup(
        IEnumerable<NuGet.Packaging.PackageDependencyGroup> dependencyGroups,
        NuGetFramework targetFramework)
    {
        var groups = dependencyGroups.ToList();
        if (groups.Count == 0)
        {
            return true;
        }

        foreach (var group in groups)
        {
            var packageFramework = group.TargetFramework;
            if (packageFramework.IsAny ||
                packageFramework.Equals(NuGetFramework.AnyFramework) ||
                DefaultCompatibilityProvider.Instance.IsCompatible(targetFramework, packageFramework))
            {
                return true;
            }
        }

        return false;
    }

    private static PackageCompatibilityInfo Failed(string packageId, string currentVersion, string targetFrameworkMoniker, string errorMessage) =>
        new()
        {
            PackageId = packageId,
            CurrentVersion = currentVersion,
            TargetFrameworkMoniker = targetFrameworkMoniker,
            CurrentVersionIsCompatible = false,
            RecommendedUpgradeVersion = null,
            LatestPublishedVersion = null,
            CheckSucceeded = false,
            ErrorMessage = errorMessage,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };

    private static string BuildCacheKey(string packageId, string currentVersion, string targetFrameworkMoniker) =>
        $"{packageId}::{currentVersion}::{targetFrameworkMoniker}";
}
