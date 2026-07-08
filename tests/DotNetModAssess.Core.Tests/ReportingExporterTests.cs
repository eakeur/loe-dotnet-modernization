using DotNetModAssess.Core.Fixtures;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Reporting;

namespace DotNetModAssess.Core.Tests;

public class ReportingExporterTests : IDisposable
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

    private readonly string _tempDir;
    private readonly SolutionModel _solution;
    private readonly DependencyGraph _graph;
    private readonly IReadOnlyList<UsageResult> _usageResults;

    public ReportingExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dnma-reporting-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        _solution = FixtureDataLoader.LoadSolution(Path.Combine(FixturesDir, "solution.json"));
        _graph = FixtureDataLoader.LoadGraph(Path.Combine(FixturesDir, "graph.json"), _solution);
        _usageResults = FixtureDataLoader.LoadUsageResults(Path.Combine(FixturesDir, "usage-results.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string TempPath(string fileName) => Path.Combine(_tempDir, fileName);

    // ---- BuildSummary ----

    [Fact]
    public void BuildSummary_ReportsProjectCountAndFormatSplit()
    {
        var summary = MarkdownJsonReportExporter.BuildSummary(_solution, _graph, _usageResults);

        Assert.Equal(3, summary.ProjectCount);
        Assert.Equal(2, summary.SdkStyleProjectCount);
        Assert.Equal(1, summary.LegacyStyleProjectCount);
    }

    [Fact]
    public void BuildSummary_DetectsNewtonsoftJsonConflictWithAllThreeVersions()
    {
        var summary = MarkdownJsonReportExporter.BuildSummary(_solution, _graph, _usageResults);

        var conflict = Assert.Single(summary.PackageVersionConflicts, c => c.PackageId == "Newtonsoft.Json");
        Assert.Equal(3, conflict.ProjectVersions.Count);

        var versions = conflict.ProjectVersions.Select(v => v.Version).OrderBy(v => v).ToList();
        var expectedVersions = new[] { "13.0.3", "13.0.1", "9.0.1" }.OrderBy(v => v);
        Assert.Equal(expectedVersions, versions);

        Assert.Contains(conflict.ProjectVersions, v => v.ProjectName == "SampleApp.Web" && v.Version == "13.0.3");
        Assert.Contains(conflict.ProjectVersions, v => v.ProjectName == "SampleApp.LegacyService" && v.Version == "9.0.1");
        Assert.Contains(conflict.ProjectVersions, v => v.ProjectName == "SampleApp.Shared" && v.Version == "13.0.1");
    }

    [Fact]
    public void BuildSummary_HasNoEvaluationErrorsButSurfacesLegacyServiceDiagnostic()
    {
        var summary = MarkdownJsonReportExporter.BuildSummary(_solution, _graph, _usageResults);

        // The fixture's LegacyService project evaluates successfully but carries one diagnostic.
        Assert.Empty(summary.ProjectEvaluationErrors);

        var diagnostic = Assert.Single(summary.ProjectDiagnostics);
        Assert.Equal("SampleApp.LegacyService", diagnostic.ProjectName);
        Assert.Equal("NU1701", diagnostic.Code);
        Assert.Contains("EntityFramework", diagnostic.Message);
    }

    [Fact]
    public void BuildSummary_CountsLegacyCouplingSignals()
    {
        var summary = MarkdownJsonReportExporter.BuildSummary(_solution, _graph, _usageResults);

        Assert.Equal(1, summary.LegacyCoupling.ProjectsReferencingSystemWeb);
        Assert.Equal(1, summary.LegacyCoupling.ProjectsReferencingSystemServiceModel);
        Assert.Equal(1, summary.LegacyCoupling.ProjectsUsingPackagesConfig);
        Assert.Equal(1, summary.LegacyCoupling.ProjectsWithComReferences);
        Assert.Equal(1, summary.LegacyCoupling.ProjectsUsingConfigurationManager);
        Assert.Equal(1, summary.LegacyCoupling.ProjectsWithAppConfig);
    }

    [Fact]
    public void BuildSummary_CountsUsageResultsBySymbolAndProject()
    {
        var summary = MarkdownJsonReportExporter.BuildSummary(_solution, _graph, _usageResults);

        Assert.Equal(5, summary.UsageResults.TotalUsageCount);
        Assert.Contains(summary.UsageResults.CountsByProject,
            c => c.ProjectPath == "src/SampleApp.LegacyService/SampleApp.LegacyService.csproj" && c.Count == 3);
        Assert.Contains(summary.UsageResults.CountsByProject,
            c => c.ProjectPath == "src/SampleApp.Web/SampleApp.Web.csproj" && c.Count == 2);
    }

    // ---- MarkdownJsonReportExporter ----

    [Fact]
    public async Task ExportAsync_Markdown_ContainsKeyFacts()
    {
        var exporter = new MarkdownJsonReportExporter();
        var path = TempPath("report.md");

        await exporter.ExportAsync(_solution, _graph, _usageResults, path);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("# Modernization Assessment Report", content);
        Assert.Contains("**Project count:** 3", content);
        Assert.Contains("Newtonsoft.Json", content);
        Assert.Contains("13.0.3", content);
        Assert.Contains("9.0.1", content);
        Assert.Contains("13.0.1", content);
        Assert.Contains("SampleApp.LegacyService", content);
        Assert.Contains("NU1701", content);
    }

    [Fact]
    public async Task ExportAsync_Json_ContainsKeyFacts()
    {
        var exporter = new MarkdownJsonReportExporter();
        var path = TempPath("report.json");

        await exporter.ExportAsync(_solution, _graph, _usageResults, path);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("\"projectCount\": 3", content);
        Assert.Contains("Newtonsoft.Json", content);
        Assert.Contains("13.0.3", content);
        Assert.Contains("NU1701", content);

        // Must be valid, parseable JSON.
        using var doc = System.Text.Json.JsonDocument.Parse(content);
        Assert.Equal(3, doc.RootElement.GetProperty("projectCount").GetInt32());
    }

    // ---- DotMermaidGraphExporter ----

    [Fact]
    public async Task ExportAsync_Dot_HasCorrectNodeAndEdgeCounts()
    {
        var exporter = new DotMermaidGraphExporter();
        var path = TempPath("graph.dot");

        await exporter.ExportAsync(_graph, path);

        var content = await File.ReadAllTextAsync(path);
        Assert.StartsWith("digraph DependencyGraph {", content.TrimStart());
        Assert.EndsWith("}", content.TrimEnd());

        // One "->" per edge.
        var arrowCount = content.Split("->").Length - 1;
        Assert.Equal(_graph.Edges.Count, arrowCount);

        // Project nodes rendered as boxes, package nodes as ellipses.
        Assert.Contains("shape=box", content);
        Assert.Contains("shape=ellipse", content);

        // The conflicted Newtonsoft.Json package must be visually flagged.
        Assert.Contains("\"Newtonsoft.Json\" [label=\"Newtonsoft.Json\", shape=ellipse, style=filled, fillcolor=\"#f8cecc\"", content);
    }

    [Fact]
    public async Task ExportAsync_Mermaid_HasCorrectNodeAndEdgeCounts()
    {
        var exporter = new DotMermaidGraphExporter();
        var path = TempPath("graph.mmd");

        await exporter.ExportAsync(_graph, path);

        var content = await File.ReadAllTextAsync(path);
        Assert.StartsWith("graph TD", content.TrimStart());

        // One node declaration line per graph node (N0.. tokens).
        for (var i = 0; i < _graph.Nodes.Count; i++)
        {
            Assert.Contains($"N{i}", content);
        }

        Assert.Contains("classDef project", content);
        Assert.Contains("classDef package", content);
        Assert.Contains("classDef packageConflict", content);
        Assert.Contains(":::packageConflict", content);

        var arrowCount = content.Split("-->").Length - 1;
        Assert.Equal(_graph.Edges.Count, arrowCount);
    }

    // ---- UsageReportExporter ----

    [Fact]
    public async Task UsageReportExporter_ListsCountsAndFileLocations()
    {
        var exporter = new UsageReportExporter();
        var path = TempPath("usage.md");

        await exporter.ExportAsync(_usageResults, path);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("# Usage Report", content);
        Assert.Contains("**Total occurrences:** 5", content);
        Assert.Contains("System.Web", content);
        Assert.Contains("OrderNotifier.cs", content);
        Assert.Contains("| 4 |", content); // line number for the using-directive occurrence
    }

    [Fact]
    public async Task UsageReportExporter_SupportsFilteringToASingleProject()
    {
        var exporter = new UsageReportExporter();
        var path = TempPath("usage-web-only.md");

        var filtered = _usageResults
            .Where(u => u.ProjectPath == "src/SampleApp.Web/SampleApp.Web.csproj")
            .ToList();

        await exporter.ExportAsync(filtered, path);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("**Total occurrences:** 2", content);
        Assert.DoesNotContain("System.ServiceModel", content);
        Assert.Contains("SampleApp.Shared", content);
    }
}
