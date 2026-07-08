using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Parsing;

public interface ISolutionParser
{
    Task<SolutionModel> ParseAsync(string solutionPath, CancellationToken cancellationToken = default);
}

public sealed class NotImplementedSolutionParser : ISolutionParser
{
    public Task<SolutionModel> ParseAsync(string solutionPath, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
