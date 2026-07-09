namespace DotNetModAssess.Core.Models;

/// <summary>
/// Result of checking one package's compatibility with a target framework (e.g. net8.0) against
/// whatever NuGet sources are configured for the solution - both whether the *currently resolved*
/// version is compatible, and, if not, the lowest newer published version that is (so an assessor
/// sees "you're on 4.2.0, upgrade to at least 6.1.0" rather than just a yes/no).
/// </summary>
public sealed class PackageCompatibilityInfo
{
    public required string PackageId { get; init; }
    public required string CurrentVersion { get; init; }
    public required string TargetFrameworkMoniker { get; init; }

    /// <summary>Whether <see cref="CurrentVersion"/> itself has a dependency group compatible with
    /// <see cref="TargetFrameworkMoniker"/> (this also covers netstandard2.x-and-below packages,
    /// since those are compatible with net8.0 consumers by NuGet's own compatibility rules -  no
    /// separate "or netstandard" check is needed).</summary>
    public bool CurrentVersionIsCompatible { get; init; }

    /// <summary>The lowest published version greater than <see cref="CurrentVersion"/> that is
    /// compatible with <see cref="TargetFrameworkMoniker"/>, if <see cref="CurrentVersionIsCompatible"/>
    /// is false and such a version exists. Null if the current version is already compatible, or no
    /// compatible newer version was found.</summary>
    public string? RecommendedUpgradeVersion { get; init; }

    /// <summary>The latest published version found, regardless of compatibility - useful context
    /// even when <see cref="RecommendedUpgradeVersion"/> is null because nothing compatible exists yet.</summary>
    public string? LatestPublishedVersion { get; init; }

    /// <summary>False if the check itself failed (network unavailable, package/version not found on
    /// any configured source, etc.) - distinct from "checked successfully and found incompatible".
    /// See <see cref="ErrorMessage"/> for why.</summary>
    public bool CheckSucceeded { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset CheckedAtUtc { get; init; }
}
