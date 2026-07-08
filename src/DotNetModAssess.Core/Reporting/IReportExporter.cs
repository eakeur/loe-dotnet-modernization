using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Reporting;

public interface IReportExporter
{
    Task ExportAsync(
        SolutionModel solution,
        DependencyGraph graph,
        IReadOnlyList<UsageResult> usageResults,
        string outputPath,
        CancellationToken cancellationToken = default);
}

public sealed class NotImplementedReportExporter : IReportExporter
{
    public Task ExportAsync(
        SolutionModel solution,
        DependencyGraph graph,
        IReadOnlyList<UsageResult> usageResults,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
