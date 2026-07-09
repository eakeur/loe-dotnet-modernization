namespace DotNetModAssess.Mcp.Tests.TestSupport;

/// <summary>Locates the repo's shared `fixtures/SampleLegacySolution` fixture from wherever the
/// test assembly happens to run from - same walk-up-to-repo-root approach
/// `DotNetModAssess.Core.Tests.BuildalyzerSolutionParserTests` already uses.</summary>
internal static class Fixture
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DotNetModAssess.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root from test base directory.");
        }
    }

    public static string SolutionPath =>
        Path.Combine(RepoRoot, "fixtures", "SampleLegacySolution", "SampleLegacySolution.sln");
}
