using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.UsageScanning;

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
///
/// "Live" re-scanning (see <see cref="StartWatching"/>): once a solution is loaded, a
/// <see cref="FileSystemWatcher"/> rooted at the solution's directory watches for changes to
/// <c>.sln</c>/<c>.slnf</c>/<c>.csproj</c>/<c>Directory.Build.props</c>/<c>.targets</c>/
/// <c>Directory.Packages.props</c>/<c>NuGet.Config</c> anywhere under it, debounced (editors/IDEs
/// often fire several change events for a single save), and triggers a re-parse via
/// <see cref="LoadSolutionAsync"/> on the same path. Re-parsing the *whole* solution rather than
/// patching a single project sounds expensive, but <c>BuildalyzerSolutionParser</c>'s per-project
/// evaluation cache (keyed by path + last-write-time) means every project that didn't actually
/// change is served from cache almost instantly - so in practice only the changed project(s) incur
/// real MSBuild evaluation cost, which is the effect Phase 7 asks for without needing a bespoke
/// single-project graph-splicing path. The existing <see cref="Changed"/> event already pushes the
/// refreshed state out to every page subscribed to it over the circuit's SignalR connection.
/// </summary>
public sealed class SolutionStateService(
    ISolutionParser parser,
    IDependencyGraphBuilder graphBuilder,
    IUsageScanner usageScanner,
    ILogger<SolutionStateService> logger) : IDisposable
{
    private static readonly (string Message, int DelayMs)[] StubbedStages =
    [
        ("Discovering projects...", 150),
        ("Evaluating MSBuild projects...", 200),
        ("Resolving NuGet packages...", 150),
        ("Building dependency graph...", 150)
    ];

    private static readonly string[] WatchedFilters =
    [
        "*.sln", "*.slnf", "*.csproj",
        "Directory.Build.props", "Directory.Build.targets",
        "Directory.Packages.props", "NuGet.Config"
    ];

    private const int DebounceMs = 500;

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    public SolutionModel? Solution { get; private set; }

    public DependencyGraph? Graph { get; private set; }

    public IReadOnlyList<UsageResult>? UsageResults { get; private set; }

    public bool IsLoading { get; private set; }

    public string? LoadingStageMessage { get; private set; }

    public string? LastLoadedPath { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Whether a <see cref="FileSystemWatcher"/> is currently active for the loaded solution.</summary>
    public bool IsWatching => _watcher is not null;

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

            LoadingStageMessage = "Scanning source for project/package usages...";
            NotifyChanged();
            var usageResults = await usageScanner.ScanAsync(solution, cancellationToken);

            Solution = solution;
            Graph = graph;
            UsageResults = usageResults;
            LastLoadedPath = solutionPath;
            StartWatching(solution.Path);
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

    /// <summary>
    /// (Re)starts watching the directory tree rooted at the loaded solution's directory. Safe to
    /// call repeatedly (e.g. loading a different solution) - any previous watcher is torn down first.
    /// </summary>
    private void StartWatching(string solutionPath)
    {
        StopWatching();

        var root = Path.GetDirectoryName(solutionPath);
        if (root is null || !Directory.Exists(root))
        {
            return;
        }

        _debounceTimer ??= new Timer(_ => OnDebounceElapsed(), null, Timeout.Infinite, Timeout.Infinite);

        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };

        foreach (var filter in WatchedFilters)
        {
            watcher.Filters.Add(filter);
        }

        watcher.Changed += OnWatchedFileEvent;
        watcher.Created += OnWatchedFileEvent;
        watcher.Deleted += OnWatchedFileEvent;
        watcher.Renamed += OnWatchedFileEvent;
        watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "File watcher for '{Root}' reported an error.", root);

        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void StopWatching()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnWatchedFileEvent(object sender, FileSystemEventArgs e) =>
        // Editors/IDEs frequently emit several change notifications for a single logical save
        // (e.g. write-to-temp-then-rename); debounce by restarting the timer on every event and
        // only acting once the filesystem has been quiet for DebounceMs.
        _debounceTimer?.Change(DebounceMs, Timeout.Infinite);

    private void OnDebounceElapsed()
    {
        if (LastLoadedPath is null)
        {
            return;
        }

        // Timer callbacks run on a thread-pool thread; LoadSolutionAsync only touches this
        // service's own state and raises Changed, and every page subscribed to Changed already
        // marshals back onto the Blazor renderer's sync context itself (InvokeAsync(StateHasChanged)),
        // so no extra dispatch is needed here.
        _ = LoadSolutionAsync(LastLoadedPath);
    }

    private void NotifyChanged() => Changed?.Invoke();

    public void Dispose()
    {
        StopWatching();
        _debounceTimer?.Dispose();
    }
}
