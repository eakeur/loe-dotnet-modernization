using DotNetModAssess.Mcp.Services;
using DotNetModAssess.Mcp.Tests.TestSupport;
using DotNetModAssess.Mcp.Tools;

namespace DotNetModAssess.Mcp.Tests;

/// <summary>Covers <see cref="WorkspaceTools"/> - see <see cref="McpWorkspaceStateTests"/> for the
/// underlying "await until ready"/never-blocks-get_load_status contract itself; these tests focus
/// on the tool methods' own output being correct against the real fixture.</summary>
public class WorkspaceToolsTests : IAsyncLifetime
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
    public void GetLoadStatus_AfterLoadCompletes_ReportsLoadedWithCorrectCounts()
    {
        var status = WorkspaceTools.GetLoadStatus(_state);

        Assert.Equal(WorkspaceLoadStatus.Loaded, status.Status);
        Assert.Equal(2, status.ProjectCount);
        Assert.True(status.IsWatching);
    }

    [Fact]
    public async Task GetSolutionOverview_ReportsExpectedSplitAndFindings()
    {
        var overview = await WorkspaceTools.GetSolutionOverview(_state);

        Assert.Equal(2, overview.ProjectCount);
        Assert.Equal(1, overview.SdkStyleProjectCount);
        Assert.Equal(1, overview.LegacyStyleProjectCount);
        Assert.Equal(1, overview.PackagesConfigProjectCount);
        Assert.Equal(1, overview.DistinctPackageCount);
        Assert.Contains(overview.LegacyFindingsByPattern, p => p.PatternName == "COM Interop");
        Assert.Contains(overview.TargetFrameworkBreakdown, t => t.TargetFramework == "net8.0");
        Assert.Contains(overview.TargetFrameworkBreakdown, t => t.TargetFramework == "v4.8");
    }

    [Fact]
    public async Task Reload_RunsSuccessfully_AndReportsLoadedAfterwards()
    {
        var status = await WorkspaceTools.Reload(_state);

        Assert.Equal(WorkspaceLoadStatus.Loaded, status.Status);
        Assert.Equal(2, status.ProjectCount);
    }
}
