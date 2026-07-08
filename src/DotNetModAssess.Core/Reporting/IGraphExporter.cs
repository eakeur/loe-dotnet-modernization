using DotNetModAssess.Core.Graph;

namespace DotNetModAssess.Core.Reporting;

public interface IGraphExporter
{
    Task ExportAsync(DependencyGraph graph, string outputPath, CancellationToken cancellationToken = default);
}

public sealed class NotImplementedGraphExporter : IGraphExporter
{
    public Task ExportAsync(DependencyGraph graph, string outputPath, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
