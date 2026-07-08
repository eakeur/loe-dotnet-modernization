using DotNetModAssess.Core.Fixtures;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;

namespace DotNetModAssess.Web.Parsing;

/// <summary>
/// Stand-in <see cref="ISolutionParser"/> used until the real Buildalyzer-backed parser
/// (being built in parallel in another worktree) is available. Ignores the requested solution
/// path entirely and always returns the hand-authored fixture solution, hydrated through the
/// shared <see cref="FixtureDataLoader"/> so pages exercise a fully-populated, real object graph.
/// </summary>
public sealed class FixtureBackedSolutionParser(ILogger<FixtureBackedSolutionParser> logger) : ISolutionParser
{
    public Task<SolutionModel> ParseAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "FixtureBackedSolutionParser ignoring requested path '{RequestedPath}'; returning fixture solution from {FixturePath} instead.",
            solutionPath,
            FixturePaths.SolutionJsonPath);

        var solution = FixtureDataLoader.LoadSolution(FixturePaths.SolutionJsonPath);
        return Task.FromResult(solution);
    }
}
