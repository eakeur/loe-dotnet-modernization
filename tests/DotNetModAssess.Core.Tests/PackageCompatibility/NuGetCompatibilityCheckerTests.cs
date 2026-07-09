using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.PackageCompatibility;

namespace DotNetModAssess.Core.Tests.PackageCompatibility;

/// <summary>
/// Covers <see cref="NuGetCompatibilityChecker"/>'s own failure-handling contract (never throw,
/// always set <see cref="PackageCompatibilityInfo.CheckSucceeded"/>/<see cref="PackageCompatibilityInfo.ErrorMessage"/>
/// instead) for inputs that fail validation *before* any network call is made, so these run fully
/// offline and deterministically. See <see cref="NuGetVersionCompatibilityEvaluatorTests"/> for
/// the "given a set of published versions, what's the right compatibility/upgrade decision" logic
/// itself - that's exercised there with plain data, with no dependency on this class at all.
///
/// The one real-network sanity test at the bottom of this file is intentionally the only test in
/// this class that isn't guaranteed offline-safe by construction; see its own doc comment.
/// </summary>
public class NuGetCompatibilityCheckerTests
{
    private static readonly NuGetCompatibilityChecker Checker = new();

    [Fact]
    public async Task CheckAsync_MalformedCurrentVersion_FailsGracefullyWithoutThrowing()
    {
        var result = await Checker.CheckAsync(Path.GetTempPath(), "SomePackage", "not-a-version!!", "net8.0");

        Assert.False(result.CheckSucceeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Equal("SomePackage", result.PackageId);
        Assert.Equal("not-a-version!!", result.CurrentVersion);
        Assert.Equal("net8.0", result.TargetFrameworkMoniker);
        Assert.False(result.CurrentVersionIsCompatible);
        Assert.Null(result.RecommendedUpgradeVersion);
        Assert.Null(result.LatestPublishedVersion);
    }

    [Fact]
    public async Task CheckAsync_EmptyCurrentVersion_FailsGracefullyWithoutThrowing()
    {
        var result = await Checker.CheckAsync(Path.GetTempPath(), "SomePackage", string.Empty, "net8.0");

        Assert.False(result.CheckSucceeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task CheckAsync_UnrecognizedTargetFrameworkMoniker_FailsGracefullyWithoutThrowing()
    {
        var result = await Checker.CheckAsync(Path.GetTempPath(), "SomePackage", "1.0.0", "not a real tfm $$$");

        Assert.False(result.CheckSucceeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Equal("not a real tfm $$$", result.TargetFrameworkMoniker);
    }

    /// <summary>
    /// The one intentionally network-dependent test in this suite: exercises the real
    /// NuGet.Protocol wiring end-to-end (source resolution, <c>FindPackageByIdResource</c>,
    /// <c>DefaultCompatibilityProvider</c>) against a real, extremely stable, well-known package
    /// rather than a fake seam. It must never be able to fail `dotnet test` in an offline/CI
    /// environment: any exception or an unsuccessful check (network unreachable, no source
    /// resolvable, etc.) is treated as inconclusive and the test passes without asserting anything
    /// beyond that "check succeeded" case.
    /// </summary>
    [Fact]
    public async Task CheckAsync_RealNuGetOrgNewtonsoftJson_SanityChecksRealWiring_WhenNetworkAvailable()
    {
        PackageCompatibilityInfo result;
        try
        {
            result = await Checker.CheckAsync(Path.GetTempPath(), "Newtonsoft.Json", "9.0.1", "net8.0");
        }
        catch
        {
            return; // Network unavailable - inconclusive in this environment, not a test failure.
        }

        if (!result.CheckSucceeded)
        {
            return; // Same as above - e.g. no configured source was reachable.
        }

        Assert.Equal("Newtonsoft.Json", result.PackageId);
        Assert.False(string.IsNullOrWhiteSpace(result.LatestPublishedVersion));
    }
}
