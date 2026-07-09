using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Runs every registered <see cref="ILegacyPatternDetector"/> across a solution and returns the
/// combined findings. Kept as its own top-level scan (distinct from <see
/// cref="DotNetModAssess.Core.UsageScanning.IUsageScanner"/>) since "known migration-blocker
/// patterns" is a different kind of question from "how much is project/package X used elsewhere" -
/// callers/UI should be able to present these as a dedicated "migration blockers" view rather than
/// mixing them into per-project/package usage counts.
/// </summary>
public interface ILegacyPatternScanner
{
    Task<IReadOnlyList<UsageResult>> ScanAsync(SolutionModel solution, CancellationToken cancellationToken = default);
}

/// <summary>Default implementation: just fans out to every registered detector and concatenates
/// results. Deliberately trivial - the actual detection logic belongs in each
/// <see cref="ILegacyPatternDetector"/>, not here.</summary>
public sealed class LegacyPatternScanner(IEnumerable<ILegacyPatternDetector> detectors) : ILegacyPatternScanner
{
    public async Task<IReadOnlyList<UsageResult>> ScanAsync(SolutionModel solution, CancellationToken cancellationToken = default)
    {
        var results = new List<UsageResult>();

        foreach (var detector in detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.AddRange(await detector.DetectAsync(solution, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
