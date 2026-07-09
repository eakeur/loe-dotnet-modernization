using DotNetModAssess.Core.Models;
using DotNetModAssess.Mcp.Services;
using DotNetModAssess.Mcp.Tests.TestSupport;
using DotNetModAssess.Mcp.Tools;
using ModelContextProtocol;

namespace DotNetModAssess.Mcp.Tests;

/// <summary>
/// Calls <see cref="ProjectTools"/>' methods directly - stripped of the <c>[McpServerTool]</c>
/// attributes and the MCP transport/JSON-RPC layer, a tool method is just a plain static C#
/// method, so it can be tested the same way as any other unit under test. Verifying the MCP
/// protocol/schema-generation/transport itself is the SDK's job, not this project's - these tests
/// only care whether the tool logic itself is correct against the real fixture data.
/// </summary>
public class ProjectToolsTests : IAsyncLifetime
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
    public async Task ListProjects_NoFilters_ReturnsBothFixtureProjects()
    {
        var result = await ProjectTools.ListProjects(_state);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "Modern.Sdk" && p.Format == ProjectFormat.SdkStyle && p.PackagesModel == PackagesModel.PackageReference);
        Assert.Contains(result, p => p.Name == "Legacy.Net48App" && p.Format == ProjectFormat.LegacyStyle && p.PackagesModel == PackagesModel.PackagesConfig);
    }

    [Fact]
    public async Task ListProjects_FilterByFormat_ReturnsOnlyMatchingProject()
    {
        var result = await ProjectTools.ListProjects(_state, format: "LegacyStyle");

        var project = Assert.Single(result);
        Assert.Equal("Legacy.Net48App", project.Name);
    }

    [Fact]
    public async Task ListProjects_FilterByReferencesSystemServiceModel_ReturnsOnlyTheLegacyProject()
    {
        var result = await ProjectTools.ListProjects(_state, referencesSystemServiceModel: true);

        var project = Assert.Single(result);
        Assert.Equal("Legacy.Net48App", project.Name);
        Assert.True(project.ReferencesSystemServiceModel);
    }

    [Fact]
    public async Task ListProjects_FilterByNameContains_IsCaseInsensitive()
    {
        // Deliberately not just "modern" - the repo itself lives under a directory named
        // "...-modernization", which would also substring-match each project's absolute Path and
        // make this assertion flaky depending on where the repo happens to be checked out.
        var result = await ProjectTools.ListProjects(_state, nameContains: "MODERN.SDK");

        var project = Assert.Single(result);
        Assert.Equal("Modern.Sdk", project.Name);
    }

    [Fact]
    public async Task GetProjectDetails_ForModernSdk_IncludesDependencyOnLegacyProject_AndPackageReference()
    {
        var modernPath = _state.Solution!.Projects.Single(p => p.Name == "Modern.Sdk").Path;

        var details = await ProjectTools.GetProjectDetails(_state, modernPath);

        Assert.Equal("Modern.Sdk", details.Name);
        Assert.Contains(details.Dependencies, d => d.Name == "Legacy.Net48App");
        Assert.Contains(details.PackageReferences, p => p.PackageId == "Newtonsoft.Json" && p.ResolvedVersion == "13.0.3");
    }

    [Fact]
    public async Task GetProjectDetails_ForLegacyProject_IncludesModernSdkAsADependent()
    {
        var legacyPath = _state.Solution!.Projects.Single(p => p.Name == "Legacy.Net48App").Path;

        var details = await ProjectTools.GetProjectDetails(_state, legacyPath);

        Assert.Equal("Legacy.Net48App", details.Name);
        Assert.Contains(details.Dependents, d => d.Name == "Modern.Sdk");
        Assert.True(details.LegacySignals.ReferencesSystemWeb);
        Assert.True(details.LegacySignals.ReferencesSystemServiceModel);
    }

    [Fact]
    public async Task GetProjectDetails_UnknownPath_ThrowsMcpException()
    {
        await Assert.ThrowsAsync<McpException>(() => ProjectTools.GetProjectDetails(_state, "does-not-exist.csproj"));
    }
}
