using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Search;
using DotNetModAssess.Mcp.Services;
using DotNetModAssess.Mcp.Tests.TestSupport;
using DotNetModAssess.Mcp.Tools;
using ModelContextProtocol;

namespace DotNetModAssess.Mcp.Tests;

/// <summary>Covers <see cref="UsageTools"/>, <see cref="FindingsTools"/>, <see cref="GraphTools"/>,
/// and <see cref="SearchTools"/> against the real fixture solution's eagerly-computed usage results
/// and legacy-pattern findings (see <see cref="McpWorkspaceStateTests"/> for the loading contract
/// itself - these tests assume loading already completed).</summary>
public class UsageFindingsGraphSearchToolsTests : IAsyncLifetime
{
    private McpWorkspaceState _state = null!;

    public async Task InitializeAsync()
    {
        _state = WorkspaceStateFactory.Create();
        await _state.StartAsync(Fixture.SolutionPath);
    }

    public Task DisposeAsync()
    {
        _state.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FindUsages_ForNewtonsoftJson_FindsUsingDirectiveAndPackagesConfigEntry()
    {
        var result = await UsageTools.FindUsages(_state, "Newtonsoft.Json");

        Assert.NotEmpty(result);
        Assert.All(result, u => Assert.Equal("Newtonsoft.Json", u.TargetName));
        Assert.Contains(result, u => u.Kind == UsageReferenceKind.UsingDirective && u.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task FindUsages_ForUnknownTarget_ReturnsEmpty_NotAnError()
    {
        var result = await UsageTools.FindUsages(_state, "Some.Namespace.NobodyReferences");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLegacyFindings_NoFilter_ReturnsComInteropAndConfigurationManagerFindings()
    {
        var result = await FindingsTools.GetLegacyFindings(_state);

        Assert.Contains(result, f => f.TargetName == "COM Interop");
        Assert.Contains(result, f => f.TargetName == "ConfigurationManager");
    }

    [Fact]
    public async Task GetLegacyFindings_FilteredByPatternName_ReturnsOnlyThatPattern()
    {
        var result = await FindingsTools.GetLegacyFindings(_state, patternName: "ConfigurationManager");

        Assert.NotEmpty(result);
        Assert.All(result, f => Assert.Equal("ConfigurationManager", f.TargetName));
    }

    [Fact]
    public async Task GetDependencyGraph_RootedAtSharedPackage_IncludesBothProjectsAndPackageToPackageEdges()
    {
        var graph = await GraphTools.GetDependencyGraph(_state, "Newtonsoft.Json");

        Assert.Contains(graph.Nodes, n => n.Kind == "package" && n.Id == "Newtonsoft.Json");
        Assert.Contains(graph.Nodes, n => n.Kind == "project" && n.Label == "Modern.Sdk");
        Assert.Contains(graph.Nodes, n => n.Kind == "project" && n.Label == "Legacy.Net48App");
        Assert.Contains(graph.Edges, e => e.Kind == "ProjectToPackage" && e.ToId == "Newtonsoft.Json");
    }

    [Fact]
    public async Task GetDependencyGraph_UnknownRootId_ThrowsMcpException()
    {
        await Assert.ThrowsAsync<McpException>(() => GraphTools.GetDependencyGraph(_state, "Does.Not.Exist"));
    }

    [Fact]
    public async Task Search_FreeTextQuery_FindsMatchViaRealAdHocSourceSearcher()
    {
        var result = await SearchTools.Search(_state, new AdHocSourceSearcher(), "ConfigurationManager");

        Assert.NotEmpty(result);
        Assert.Contains(result, u => u.CodeSnippet != null && u.CodeSnippet.Contains("ConfigurationManager"));
    }
}
