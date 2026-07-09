using DotNetModAssess.Mcp.Services;
using DotNetModAssess.Mcp.Tests.TestSupport;
using DotNetModAssess.Mcp.Tools;

namespace DotNetModAssess.Mcp.Tests;

/// <summary>
/// <see cref="PackageTools.CheckPackageCompatibility"/> is deliberately not covered here - it wraps
/// a live NuGet network call (see its own doc comment: on-demand per call, not precomputed), which
/// would make a unit test dependent on network access/nuget.org availability. It was verified
/// manually instead by driving the real MCP stdio process end-to-end (see this task's final
/// report) - that exercise did successfully return a real, live compatibility result for
/// Newtonsoft.Json 13.0.3 against net8.0.
/// </summary>
public class PackageToolsTests : IAsyncLifetime
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
    public async Task ListPackages_ReturnsNewtonsoftJson_SharedByBothProjects_WithNoVersionConflict()
    {
        var result = await PackageTools.ListPackages(_state);

        var package = Assert.Single(result);
        Assert.Equal("Newtonsoft.Json", package.PackageId);
        Assert.False(package.HasVersionConflict);
        Assert.Equal(2, package.ProjectVersions.Count);
        Assert.All(package.ProjectVersions, v => Assert.Equal("13.0.3", v.Version));
    }
}
