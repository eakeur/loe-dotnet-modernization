using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.UsageScanning;

public interface IUsageScanner
{
    Task<IReadOnlyList<UsageResult>> ScanAsync(SolutionModel solution, CancellationToken cancellationToken = default);
}

public sealed class NotImplementedUsageScanner : IUsageScanner
{
    public Task<IReadOnlyList<UsageResult>> ScanAsync(SolutionModel solution, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
