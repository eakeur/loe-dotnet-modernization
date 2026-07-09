using DotNetModAssess.Core.PackageCompatibility;
using NuGet.Versioning;

namespace DotNetModAssess.Core.Tests.PackageCompatibility;

public class NuGetVersionCompatibilityEvaluatorTests
{
    [Fact]
    public void Evaluate_CurrentVersionAlreadyCompatible_ReturnsNoRecommendedUpgrade()
    {
        var current = NuGetVersion.Parse("6.0.0");
        var allVersions = new[]
        {
            NuGetVersion.Parse("5.0.0"),
            NuGetVersion.Parse("6.0.0"),
            NuGetVersion.Parse("7.0.0"),
        };

        // Only "6.0.0" (the current version) is ever asked about - Evaluate should short-circuit
        // once it finds the current version compatible and never need to consult the recommended-
        // upgrade search at all.
        var result = NuGetVersionCompatibilityEvaluator.Evaluate(
            current,
            allVersions,
            v => v == current);

        Assert.True(result.CurrentVersionIsCompatible);
        Assert.Null(result.RecommendedUpgradeVersion);
        Assert.Equal("7.0.0", result.LatestPublishedVersion);
    }

    [Fact]
    public void Evaluate_CurrentIncompatibleWithCompatibleNewerVersionAvailable_RecommendsMinimalCompatibleVersion()
    {
        var current = NuGetVersion.Parse("3.0.0");
        var allVersions = new[]
        {
            NuGetVersion.Parse("3.0.0"),
            NuGetVersion.Parse("4.0.0"), // incompatible - should be skipped
            NuGetVersion.Parse("5.0.0"), // first compatible - this is the minimal recommendation
            NuGetVersion.Parse("6.0.0"), // also compatible, but not the minimal one
        };
        var compatibleVersions = new HashSet<NuGetVersion> { NuGetVersion.Parse("5.0.0"), NuGetVersion.Parse("6.0.0") };

        var result = NuGetVersionCompatibilityEvaluator.Evaluate(
            current,
            allVersions,
            v => compatibleVersions.Contains(v));

        Assert.False(result.CurrentVersionIsCompatible);
        Assert.Equal("5.0.0", result.RecommendedUpgradeVersion);
        Assert.Equal("6.0.0", result.LatestPublishedVersion);
    }

    [Fact]
    public void Evaluate_CurrentIncompatibleWithNothingCompatiblePublished_RecommendedUpgradeIsNullButLatestIsSet()
    {
        var current = NuGetVersion.Parse("1.0.0");
        var allVersions = new[]
        {
            NuGetVersion.Parse("1.0.0"),
            NuGetVersion.Parse("1.1.0"),
            NuGetVersion.Parse("1.2.0"),
        };

        var result = NuGetVersionCompatibilityEvaluator.Evaluate(
            current,
            allVersions,
            _ => false);

        Assert.False(result.CurrentVersionIsCompatible);
        Assert.Null(result.RecommendedUpgradeVersion);
        Assert.Equal("1.2.0", result.LatestPublishedVersion);
    }

    [Fact]
    public void Evaluate_NoPublishedVersionsAtAll_LatestPublishedVersionIsNull()
    {
        var current = NuGetVersion.Parse("1.0.0");

        var result = NuGetVersionCompatibilityEvaluator.Evaluate(
            current,
            Array.Empty<NuGetVersion>(),
            _ => false);

        Assert.False(result.CurrentVersionIsCompatible);
        Assert.Null(result.RecommendedUpgradeVersion);
        Assert.Null(result.LatestPublishedVersion);
    }

    [Fact]
    public void Evaluate_OnlyConsidersVersionsStrictlyGreaterThanCurrent_ForRecommendedUpgrade()
    {
        var current = NuGetVersion.Parse("2.0.0");
        var allVersions = new[]
        {
            NuGetVersion.Parse("1.0.0"), // compatible, but older than current - must not be recommended
            NuGetVersion.Parse("2.0.0"),
            NuGetVersion.Parse("3.0.0"), // compatible and newer - this is the recommendation
        };

        var result = NuGetVersionCompatibilityEvaluator.Evaluate(
            current,
            allVersions,
            v => v != current);

        Assert.False(result.CurrentVersionIsCompatible);
        Assert.Equal("3.0.0", result.RecommendedUpgradeVersion);
        Assert.Equal("3.0.0", result.LatestPublishedVersion);
    }
}
