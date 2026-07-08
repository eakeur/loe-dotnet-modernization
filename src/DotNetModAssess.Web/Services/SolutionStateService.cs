using DotNetModAssess.Core.Fixtures;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Web.Parsing;

namespace DotNetModAssess.Web.Services;

/// <summary>
/// Owns the "currently loaded" solution + dependency graph for a single Blazor Server circuit
/// and exposes them to pages.
///
/// Registered as <c>Scoped</c> (see Program.cs), not Singleton: in Blazor Server, one DI scope
/// is created per circuit (i.e. per connected browser tab), so a Scoped service naturally gives
/// each assessor's session its own independently loaded solution instead of one shared global
/// mutable state that different users/tabs would stomp on. It's also not Transient because pages
/// and their child components within the same circuit need to see the same loaded state.
///
/// <see cref="LoadSolutionAsync"/> accepts a path argument for forward compatibility with the
/// real parser, but since it's fixture-backed for now, the path is effectively ignored (it is
/// still passed through to <see cref="ISolutionParser.ParseAsync"/> and logged there).
///
/// Progress reporting is intentionally minimal/stubbed: real ingestion (MSBuild evaluation,
/// NuGet resolution, graph building) will be long-running and should report granular progress
/// over a SignalR circuit; here we simulate a few named stages with short delays so the page
/// structure and data-binding for a progress UI is already in place for the real parser to plug
/// into later.
/// </summary>
public sealed class SolutionStateService(ISolutionParser parser, ILogger<SolutionStateService> logger)
{
    private static readonly (string Message, int DelayMs)[] StubbedStages =
    [
        ("Discovering projects...", 150),
        ("Evaluating MSBuild projects...", 200),
        ("Resolving NuGet packages...", 150),
        ("Building dependency graph...", 150)
    ];

    public SolutionModel? Solution { get; private set; }

    public DependencyGraph? Graph { get; private set; }

    public bool IsLoading { get; private set; }

    public string? LoadingStageMessage { get; private set; }

    public string? LastLoadedPath { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Raised whenever loading state, progress, or the loaded data changes.</summary>
    public event Action? Changed;

    public async Task LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        LastError = null;
        NotifyChanged();

        try
        {
            foreach (var (message, delayMs) in StubbedStages)
            {
                LoadingStageMessage = message;
                NotifyChanged();
                await Task.Delay(delayMs, cancellationToken);
            }

            var solution = await parser.ParseAsync(solutionPath, cancellationToken);
            var graph = FixtureDataLoader.LoadGraph(FixturePaths.GraphJsonPath, solution);

            Solution = solution;
            Graph = graph;
            LastLoadedPath = solutionPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load solution from '{SolutionPath}'.", solutionPath);
            LastError = ex.Message;
        }
        finally
        {
            IsLoading = false;
            LoadingStageMessage = null;
            NotifyChanged();
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}
