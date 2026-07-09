using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Search;

/// <summary>
/// Runs an arbitrary, caller-supplied text query across a loaded solution's source, for a
/// free-text "search panel" UI - as opposed to <c>UsageScanning</c>'s scanners, which only ever
/// search for a fixed, solution-derived target list (project namespaces / package ids) or
/// <c>LegacyPatterns</c>' detectors, which only ever look for specific known idioms. Results reuse
/// the same <see cref="UsageResult"/> shape everything else in this codebase already presents.
/// </summary>
public interface IAdHocSourceSearcher
{
    Task<IReadOnlyList<UsageResult>> SearchAsync(SolutionModel solution, string query, CancellationToken cancellationToken = default);
}
