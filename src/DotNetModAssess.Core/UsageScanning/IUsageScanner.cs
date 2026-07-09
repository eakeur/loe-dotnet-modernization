using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.UsageScanning;

public interface IUsageScanner
{
    /// <param name="progress">Optional sink for granular status updates (e.g. which file is
    /// currently being scanned) - see <see cref="DotNetModAssess.Core.Parsing.ISolutionParser.ParseAsync"/>
    /// for the same convention.</param>
    Task<IReadOnlyList<UsageResult>> ScanAsync(SolutionModel solution, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class NotImplementedUsageScanner : IUsageScanner
{
    public Task<IReadOnlyList<UsageResult>> ScanAsync(SolutionModel solution, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
