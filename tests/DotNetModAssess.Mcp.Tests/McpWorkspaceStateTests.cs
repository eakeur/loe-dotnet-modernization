using DotNetModAssess.Mcp.Services;
using DotNetModAssess.Mcp.Tests.TestSupport;
using DotNetModAssess.Mcp.Tools;

namespace DotNetModAssess.Mcp.Tests;

/// <summary>
/// Exercises <see cref="McpWorkspaceState"/>'s background-loading contract directly - the whole
/// point of this server's design (see the Mcp project's own doc comments / the task brief): the
/// MCP transport must never block on a slow parse, and every data tool must transparently wait for
/// an in-flight load rather than needing its own retry logic. These tests use the real
/// Buildalyzer-backed parser/graph-builder/usage-scanner/legacy-pattern-scanner against the shared
/// `fixtures/SampleLegacySolution` fixture (2 real projects: an SDK-style net8.0 project and a
/// legacy net48 project on packages.config), not fakes - only the *timing* of the parse is
/// test-controlled (via <see cref="GatedSolutionParser"/>), not its actual behavior/output.
/// </summary>
public class McpWorkspaceStateTests
{
    [Fact]
    public async Task StartAsync_LoadsRealFixtureSolution_AndPopulatesAllExpectedData()
    {
        using var state = WorkspaceStateFactory.Create();

        await state.StartAsync(Fixture.SolutionPath);

        Assert.Equal(WorkspaceLoadStatus.Loaded, state.Status);
        Assert.Null(state.LastError);
        Assert.NotNull(state.Solution);
        Assert.Equal(2, state.Solution!.Projects.Count);
        Assert.NotNull(state.Graph);
        Assert.NotNull(state.UsageResults);
        Assert.NotNull(state.LegacyFindings);
        Assert.True(state.IsWatching);
        Assert.Equal(Fixture.SolutionPath, state.LastLoadedPath);
        Assert.NotNull(state.LastLoadCompletedUtc);
    }

    [Fact]
    public async Task LoadingTask_NeverFaults_EvenWhenTheSolutionPathDoesNotExist()
    {
        using var state = WorkspaceStateFactory.Create();

        // Awaiting LoadingTask must never throw - failures are captured in Status/LastError so
        // every data tool can safely `await state.LoadingTask` without a try/catch of its own.
        await state.StartAsync("/this/path/does/not/exist.sln");

        Assert.Equal(WorkspaceLoadStatus.Failed, state.Status);
        Assert.NotNull(state.LastError);
        Assert.Null(state.Solution);
    }

    [Fact]
    public async Task GetLoadStatusSnapshot_ReturnsImmediately_WithoutAwaitingLoading()
    {
        var (state, gate) = WorkspaceStateFactory.CreateGated();
        using var _ = state;

        // Kick off loading without awaiting - exactly what Program.cs does at startup, before the
        // MCP transport starts accepting connections.
        var loadTask = state.StartAsync(Fixture.SolutionPath);

        // The gate is still closed, so the real parse cannot have finished yet - GetLoadStatusSnapshot
        // must still report Loading rather than blocking until the gate opens.
        var snapshot = state.GetLoadStatusSnapshot();
        Assert.Equal(WorkspaceLoadStatus.Loading, snapshot.Status);
        Assert.False(loadTask.IsCompleted);

        gate.Release();
        await loadTask;

        var finalSnapshot = state.GetLoadStatusSnapshot();
        Assert.Equal(WorkspaceLoadStatus.Loaded, finalSnapshot.Status);
        Assert.Equal(2, finalSnapshot.ProjectCount);
    }

    /// <summary>
    /// The key behavior this whole design exists for: a tool invoked before loading has finished
    /// must transparently wait for it, then return the same correct data it would have returned had
    /// it been called after loading naturally completed - never empty/partial/wrong data, and never
    /// needing its own bespoke "not ready yet" handling.
    /// </summary>
    [Fact]
    public async Task DataTool_CalledBeforeLoadFinishes_WaitsForLoad_ThenReturnsCorrectData()
    {
        var (state, gate) = WorkspaceStateFactory.CreateGated();
        using var _ = state;

        var loadTask = state.StartAsync(Fixture.SolutionPath);

        // Call a real tool method concurrently with the still-gated (i.e. not yet finished) load.
        var overviewTask = WorkspaceTools.GetSolutionOverview(state);

        // Give the tool's internal `await state.LoadingTask` a moment to actually start waiting,
        // then prove it really is still waiting (best-effort timing assertion; the correctness
        // assertions below don't depend on it).
        await Task.Delay(50);
        Assert.False(overviewTask.IsCompleted, "The tool call should still be blocked on the gated load.");
        Assert.False(loadTask.IsCompleted);

        gate.Release();

        var overview = await overviewTask;

        Assert.Equal(2, overview.ProjectCount);
        Assert.Equal(1, overview.SdkStyleProjectCount);
        Assert.Equal(1, overview.LegacyStyleProjectCount);
        Assert.Equal(WorkspaceLoadStatus.Loaded, state.Status);
    }

    [Fact]
    public async Task ReloadAsync_ReRunsTheLoad_AndRefreshesLastLoadCompletedUtc()
    {
        using var state = WorkspaceStateFactory.Create();
        await state.StartAsync(Fixture.SolutionPath);

        var firstCompletedAt = state.LastLoadCompletedUtc;
        Assert.NotNull(firstCompletedAt);

        await Task.Delay(10);
        await state.ReloadAsync();

        Assert.Equal(WorkspaceLoadStatus.Loaded, state.Status);
        Assert.True(state.LastLoadCompletedUtc > firstCompletedAt);
        Assert.Equal(2, state.Solution!.Projects.Count);
    }

    [Fact]
    public async Task ReloadAsync_BeforeStartAsync_ThrowsInvalidOperationException()
    {
        using var state = WorkspaceStateFactory.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => state.ReloadAsync());
    }
}
