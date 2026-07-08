using DotNetModAssess.Core.Fixtures;

namespace DotNetModAssess.Core.Tests;

public class FixtureDataLoaderTests
{
    private static string FixturesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DotNetModAssess.sln")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new InvalidOperationException("Could not locate repo root (DotNetModAssess.sln) from test base directory.");
            }

            return Path.Combine(dir.FullName, "fixtures", "sample-solution-basic");
        }
    }

    [Fact]
    public void LoadSolution_HydratesProjectsWithRealProjectReferences()
    {
        var solution = FixtureDataLoader.LoadSolution(Path.Combine(FixturesDir, "solution.json"));

        Assert.Equal(3, solution.Projects.Count);

        var web = solution.Projects.Single(p => p.Name == "SampleApp.Web");
        var shared = solution.Projects.Single(p => p.Name == "SampleApp.Shared");

        Assert.Single(web.ProjectReferences);
        Assert.Same(shared, web.ProjectReferences[0]);
    }

    [Fact]
    public void LoadGraph_DetectsNewtonsoftJsonVersionConflict()
    {
        var solution = FixtureDataLoader.LoadSolution(Path.Combine(FixturesDir, "solution.json"));
        var graph = FixtureDataLoader.LoadGraph(Path.Combine(FixturesDir, "graph.json"), solution);

        var newtonsoft = graph.Nodes
            .OfType<DotNetModAssess.Core.Graph.PackageGraphNode>()
            .Single(n => n.Package.PackageId == "Newtonsoft.Json");

        Assert.True(newtonsoft.Package.HasVersionConflict);
        Assert.Equal(3, newtonsoft.Package.ProjectVersions.Count);
    }

    [Fact]
    public void LoadUsageResults_ReturnsFlatList()
    {
        var results = FixtureDataLoader.LoadUsageResults(Path.Combine(FixturesDir, "usage-results.json"));

        Assert.NotEmpty(results);
    }
}
