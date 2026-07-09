using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Parsing;

public interface ISolutionParser
{
    /// <param name="progress">
    /// Optional sink for human-readable, granular status updates as parsing proceeds (e.g. which
    /// project is currently being evaluated, out of how many) - callers driving a "loading..." UI
    /// for a large solution should pass one rather than showing a single static message for the
    /// whole (potentially long) parse. Implementations should report at whatever granularity is
    /// actually meaningful for the work they do; callers must not assume any particular message
    /// format or reporting frequency.
    /// </param>
    Task<SolutionModel> ParseAsync(string solutionPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class NotImplementedSolutionParser : ISolutionParser
{
    public Task<SolutionModel> ParseAsync(string solutionPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
