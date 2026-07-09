using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;

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
/// <see cref="LoadSolutionAsync"/> parses whatever <c>.sln</c>/<c>.slnf</c> path is passed in via
/// the real <see cref="ISolutionParser"/> (Buildalyzer-backed) and builds a real
/// <see cref="DependencyGraph"/> for it via <see cref="IDependencyGraphBuilder"/> - both are real
/// implementations now that Phases 1 and 2 have landed.
///
/// Progress reporting is intentionally minimal/stubbed: real ingestion (MSBuild evaluation,
/// NuGet resolution, graph building) is long-running and should eventually report granular
/// progress over the SignalR circuit as each project is evaluated; here we simulate a few named
/// stages with short delays so the page structure and data-binding for a progress UI is already
/// in place for that finer-grained reporting to plug into later.
/// </summary>
public sealed class SolutionStateService(
    ISolutionParser parser,
    IDependencyGraphBuilder graphBuilder,
    ILogger<SolutionStateService> logger)
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
            var graph = graphBuilder.Build(solution);

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
