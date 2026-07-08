namespace DotNetModAssess.Web.Parsing;

/// <summary>
/// Locates the checked-in fixture data set used to stand in for a real parsed solution until
/// the Buildalyzer-backed <c>ISolutionParser</c> lands. Walks up from the app's base directory
/// until it finds the repo root marker (DotNetModAssess.sln), mirroring the pattern used by
/// tests/DotNetModAssess.Core.Tests/UnitTest1.cs so this works both under `dotnet run` and in
/// published/test output layouts.
/// </summary>
internal static class FixturePaths
{
    private const string FixtureSetName = "sample-solution-basic";

    public static string SolutionJsonPath => Path.Combine(ResolveFixtureDirectory(), "solution.json");

    public static string GraphJsonPath => Path.Combine(ResolveFixtureDirectory(), "graph.json");

    private static string ResolveFixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DotNetModAssess.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root (DotNetModAssess.sln) from base directory '{AppContext.BaseDirectory}'.");
        }

        return Path.Combine(dir.FullName, "fixtures", FixtureSetName);
    }
}
