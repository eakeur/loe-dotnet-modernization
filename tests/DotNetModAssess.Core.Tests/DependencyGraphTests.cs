using DotNetModAssess.Core.Fixtures;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests;

public class DependencyGraphTests
{
    private const string WebPath = "src/SampleApp.Web/SampleApp.Web.csproj";
    private const string LegacyServicePath = "src/SampleApp.LegacyService/SampleApp.LegacyService.csproj";
    private const string SharedPath = "src/SampleApp.Shared/SampleApp.Shared.csproj";

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

    private static SolutionModel LoadSolution() =>
        FixtureDataLoader.LoadSolution(Path.Combine(FixturesDir, "solution.json"));

    private static DependencyGraph LoadGoldenGraph(SolutionModel solution) =>
        FixtureDataLoader.LoadGraph(Path.Combine(FixturesDir, "graph.json"), solution);

    private static DependencyGraph BuildGraph(SolutionModel solution) =>
        new DependencyGraphBuilder().Build(solution);

    [Fact]
    public void Build_ProducesSameNodeIdsAsGoldenGraph()
    {
        var solution = LoadSolution();
        var golden = LoadGoldenGraph(solution);
        var built = BuildGraph(solution);

        var goldenProjectIds = golden.Nodes.OfType<ProjectGraphNode>().Select(n => n.Id).OrderBy(x => x);
        var builtProjectIds = built.Nodes.OfType<ProjectGraphNode>().Select(n => n.Id).OrderBy(x => x);
        Assert.Equal(goldenProjectIds, builtProjectIds);

        var goldenPackageIds = golden.Nodes.OfType<PackageGraphNode>().Select(n => n.Id).OrderBy(x => x);
        var builtPackageIds = built.Nodes.OfType<PackageGraphNode>().Select(n => n.Id).OrderBy(x => x);
        Assert.Equal(goldenPackageIds, builtPackageIds);
    }

    [Fact]
    public void Build_MatchesGoldenGraph_HasVersionConflictPerPackage()
    {
        var solution = LoadSolution();
        var golden = LoadGoldenGraph(solution);
        var built = BuildGraph(solution);

        var goldenByPackageId = golden.Nodes.OfType<PackageGraphNode>().ToDictionary(n => n.Id, n => n.Package.HasVersionConflict);
        var builtByPackageId = built.Nodes.OfType<PackageGraphNode>().ToDictionary(n => n.Id, n => n.Package.HasVersionConflict);

        Assert.Equal(goldenByPackageId, builtByPackageId);
    }

    [Fact]
    public void Build_DetectsThreeWayNewtonsoftJsonVersionConflict()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);

        var newtonsoft = built.Nodes.OfType<PackageGraphNode>().Single(n => n.Id == "Newtonsoft.Json");

        Assert.True(newtonsoft.Package.HasVersionConflict);
        Assert.Equal(3, newtonsoft.Package.ProjectVersions.Count);
        var versionsByProject = newtonsoft.Package.ProjectVersions.ToDictionary(pv => pv.ProjectPath, pv => pv.Version);
        Assert.Equal("13.0.3", versionsByProject[WebPath]);
        Assert.Equal("9.0.1", versionsByProject[LegacyServicePath]);
        Assert.Equal("13.0.1", versionsByProject[SharedPath]);
    }

    [Fact]
    public void Build_NonConflictingPackages_HaveNoConflictFlag()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);

        var packagesById = built.Nodes.OfType<PackageGraphNode>().ToDictionary(n => n.Id);

        Assert.False(packagesById["Microsoft.EntityFrameworkCore.SqlServer"].Package.HasVersionConflict);
        Assert.False(packagesById["Serilog.AspNetCore"].Package.HasVersionConflict);
        Assert.False(packagesById["EntityFramework"].Package.HasVersionConflict);
    }

    [Fact]
    public void Build_MatchesGoldenGraph_EdgesWithResolvedVersions()
    {
        var solution = LoadSolution();
        var golden = LoadGoldenGraph(solution);
        var built = BuildGraph(solution);

        var goldenEdges = golden.Edges
            .Select(e => (e.Kind, e.FromId, e.ToId, e.ResolvedPackageVersion))
            .OrderBy(e => e.FromId).ThenBy(e => e.ToId)
            .ToList();
        var builtEdges = built.Edges
            .Select(e => (e.Kind, e.FromId, e.ToId, e.ResolvedPackageVersion))
            .OrderBy(e => e.FromId).ThenBy(e => e.ToId)
            .ToList();

        Assert.Equal(goldenEdges, builtEdges);
    }

    [Fact]
    public void Build_CreatesProjectToProjectEdges()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);

        Assert.Contains(built.Edges, e =>
            e.Kind == GraphEdgeKind.ProjectToProject && e.FromId == WebPath && e.ToId == SharedPath);
        Assert.Contains(built.Edges, e =>
            e.Kind == GraphEdgeKind.ProjectToProject && e.FromId == LegacyServicePath && e.ToId == SharedPath);
    }

    [Fact]
    public void GetDependencies_Web_ReturnsSharedProjectAndItsPackages()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);
        var webNode = built.Nodes.Single(n => n.Id == WebPath);

        var dependencies = built.GetDependencies(webNode);
        var dependencyIds = dependencies.Select(n => n.Id).ToHashSet();

        Assert.Contains(SharedPath, dependencyIds);
        Assert.Contains("Newtonsoft.Json", dependencyIds);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", dependencyIds);
        Assert.Contains("Serilog.AspNetCore", dependencyIds);
        Assert.Equal(4, dependencies.Count);
    }

    [Fact]
    public void GetDependents_Web_IsEmpty_NothingReferencesIt()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);
        var webNode = built.Nodes.Single(n => n.Id == WebPath);

        Assert.Empty(built.GetDependents(webNode));
    }

    [Fact]
    public void GetDependents_Shared_IncludesWebAndLegacyService()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);
        var sharedNode = built.Nodes.Single(n => n.Id == SharedPath);

        var dependents = built.GetDependents(sharedNode);
        var dependentIds = dependents.Select(n => n.Id).ToHashSet();

        Assert.Equal(2, dependents.Count);
        Assert.Contains(WebPath, dependentIds);
        Assert.Contains(LegacyServicePath, dependentIds);
    }

    [Fact]
    public void GetDependents_NewtonsoftJson_IncludesAllThreeProjects()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);
        var newtonsoftNode = built.Nodes.Single(n => n.Id == "Newtonsoft.Json");

        var dependents = built.GetDependents(newtonsoftNode);
        var dependentIds = dependents.Select(n => n.Id).ToHashSet();

        Assert.Equal(3, dependents.Count);
        Assert.Contains(WebPath, dependentIds);
        Assert.Contains(LegacyServicePath, dependentIds);
        Assert.Contains(SharedPath, dependentIds);
    }

    [Fact]
    public void GetTransitiveClosure_Forward_FromWeb_IncludesSharedAndAllTransitivePackages()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);
        var webNode = built.Nodes.Single(n => n.Id == WebPath);

        var closure = built.GetTransitiveClosure(webNode, GraphDirection.Forward);
        var closureIds = closure.Select(n => n.Id).ToHashSet();

        // Direct: SampleApp.Shared, Newtonsoft.Json (Web's version), EFCore.SqlServer, Serilog.AspNetCore.
        // Transitive via Shared: Newtonsoft.Json (already present as the same package node).
        Assert.Contains(SharedPath, closureIds);
        Assert.Contains("Newtonsoft.Json", closureIds);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", closureIds);
        Assert.Contains("Serilog.AspNetCore", closureIds);

        // Web does not depend on LegacyService or EntityFramework (LegacyService's package).
        Assert.DoesNotContain(LegacyServicePath, closureIds);
        Assert.DoesNotContain("EntityFramework", closureIds);

        // Starting node itself is excluded.
        Assert.DoesNotContain(WebPath, closureIds);

        Assert.Equal(4, closureIds.Count);
    }

    [Fact]
    public void GetTransitiveClosure_Reverse_FromSharedPackageNewtonsoft_IncludesAllDependents()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);
        var newtonsoftNode = built.Nodes.Single(n => n.Id == "Newtonsoft.Json");

        var closure = built.GetTransitiveClosure(newtonsoftNode, GraphDirection.Reverse);
        var closureIds = closure.Select(n => n.Id).ToHashSet();

        // Directly: all three projects reference Newtonsoft.Json.
        Assert.Contains(WebPath, closureIds);
        Assert.Contains(LegacyServicePath, closureIds);
        Assert.Contains(SharedPath, closureIds);

        Assert.DoesNotContain("Newtonsoft.Json", closureIds);
    }

    [Fact]
    public void GetTransitiveClosure_Forward_FromSharedProject_IncludesOnlyItsOwnPackage()
    {
        var solution = LoadSolution();
        var built = BuildGraph(solution);
        var sharedNode = built.Nodes.Single(n => n.Id == SharedPath);

        var closure = built.GetTransitiveClosure(sharedNode, GraphDirection.Forward);
        var closureIds = closure.Select(n => n.Id).ToHashSet();

        Assert.Single(closureIds);
        Assert.Contains("Newtonsoft.Json", closureIds);
    }
}
