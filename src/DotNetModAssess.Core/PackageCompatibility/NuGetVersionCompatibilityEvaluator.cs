using NuGet.Versioning;

namespace DotNetModAssess.Core.PackageCompatibility;

/// <summary>
/// Pure "given the current version, every published version, and a way to tell whether any one
/// version is compatible with the target framework, what should
/// <see cref="Models.PackageCompatibilityInfo.CurrentVersionIsCompatible"/>,
/// <see cref="Models.PackageCompatibilityInfo.RecommendedUpgradeVersion"/> and
/// <see cref="Models.PackageCompatibilityInfo.LatestPublishedVersion"/> be" decision logic,
/// deliberately kept free of any NuGet.Protocol/network types so it's directly unit-testable with
/// plain <see cref="NuGetVersion"/> data and a canned compatibility lookup - see
/// <c>NuGetCompatibilityCheckerTests</c>/<c>NuGetVersionCompatibilityEvaluatorTests</c>.
///
/// <see cref="NuGetCompatibilityChecker"/> mirrors this exact algorithm (ascending scan for the
/// first compatible version newer than the current one, short-circuiting once found) in its own
/// async orchestration rather than calling this method directly, because the real per-version
/// compatibility check is an async NuGet.Protocol call (<c>FindPackageByIdResource.GetDependencyInfoAsync</c>)
/// and can't be threaded through the synchronous <paramref name="isVersionCompatible"/> predicate
/// used here without blocking on async work - a bad practice in an ASP.NET/Blazor Server context.
/// This method exists so the *shape* of that algorithm has one canonical, deterministic,
/// offline-testable definition.
/// </summary>
internal static class NuGetVersionCompatibilityEvaluator
{
    public readonly record struct EvaluationResult(
        bool CurrentVersionIsCompatible,
        string? RecommendedUpgradeVersion,
        string? LatestPublishedVersion);

    /// <param name="currentVersion">The version currently resolved/requested for this package.</param>
    /// <param name="allPublishedVersions">Every version found across the configured NuGet sources - used only for <see cref="EvaluationResult.LatestPublishedVersion"/>, regardless of compatibility.</param>
    /// <param name="isVersionCompatible">
    /// Whether a given version's dependency groups are compatible with the target framework.
    /// Invoked once for <paramref name="currentVersion"/> and, only if that's false, at most as
    /// many additional times as needed - in ascending order starting just above
    /// <paramref name="currentVersion"/> - to find the first compatible one
    /// (<c>Enumerable.FirstOrDefault(predicate)</c> short-circuits, so a caller whose predicate
    /// does real (expensive) work per call never pays for versions past the first compatible one).
    /// </param>
    public static EvaluationResult Evaluate(
        NuGetVersion currentVersion,
        IReadOnlyCollection<NuGetVersion> allPublishedVersions,
        Func<NuGetVersion, bool> isVersionCompatible)
    {
        var latest = allPublishedVersions.Count == 0
            ? null
            : allPublishedVersions.Max()!.ToNormalizedString();

        if (isVersionCompatible(currentVersion))
        {
            return new EvaluationResult(true, null, latest);
        }

        var recommended = allPublishedVersions
            .Where(v => v > currentVersion)
            .OrderBy(v => v)
            .FirstOrDefault(isVersionCompatible);

        return new EvaluationResult(false, recommended?.ToNormalizedString(), latest);
    }
}
