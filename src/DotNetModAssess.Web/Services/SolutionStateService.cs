using System.Diagnostics;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.UsageScanning;
using DotNetModAssess.Core.Workspaces;

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
/// <see cref="LoadSolutionAsync"/> parses whatever <c>.sln</c>/<c>.slnf</c>/standalone project file
/// is passed in via the real <see cref="ISolutionParser"/> (Buildalyzer-backed). <see cref="Graph"/>
/// is built lazily, on first access, rather than eagerly here - constructing the full
/// <see cref="DependencyGraph"/> is cheap (pure in-memory LINQ over already-parsed
/// ProjectModel/PackageReferenceModel data, no I/O), so laziness costs nothing and means a
/// workspace that's opened and closed without ever needing package/graph data never pays even that
/// small cost.
///
/// Progress reporting is real, not simulated: <see cref="ISolutionParser.ParseAsync"/>,
/// <see cref="IUsageScanner.ScanAsync"/>, and <see cref="ILegacyPatternScanner.ScanAsync"/> all
/// accept an <see cref="IProgress{T}"/> that this service wires directly to
/// <see cref="LoadingStageMessage"/> + <see cref="Changed"/>, so a large solution's genuinely-slow
/// MSBuild evaluation phase shows live per-project status instead of a single static label that
/// makes it look stuck.
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
    ILegacyPatternScanner legacyPatternScanner,
    IRecentWorkspacesStore recentWorkspacesStore,
    ILogger<SolutionStateService> logger) : IDisposable
{
    private static readonly string[] WatchedFilters =
    [
        "*.sln", "*.slnf", "*.csproj",
        "Directory.Build.props", "Directory.Build.targets",
        "Directory.Packages.props", "NuGet.Config"
    ];

    private const int DebounceMs = 500;

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    private DependencyGraph? _graphCache;

    public SolutionModel? Solution { get; private set; }

    /// <summary>
    /// Lazily built from <see cref="Solution"/> on first access via the injected
    /// <see cref="IDependencyGraphBuilder"/>, then cached until the next
    /// <see cref="LoadSolutionAsync"/>/<see cref="CloseSolution"/> invalidates it. See this class's
    /// own doc comment for why laziness here, despite building the graph being cheap either way.
    /// </summary>
    public DependencyGraph? Graph
    {
        get
        {
            if (_graphCache is null && Solution is not null)
            {
                _graphCache = graphBuilder.Build(Solution);
            }

            return _graphCache;
        }
    }

    public IReadOnlyList<UsageResult>? UsageResults { get; private set; }

    /// <summary>Legacy-pattern (WCF/WPF/ConfigurationManager/AppDomain/COM Interop) findings for
    /// the currently loaded solution, scanned eagerly alongside everything else in
    /// <see cref="LoadSolutionAsync"/> so the Overview panel's findings summary and each
    /// project/package panel's "findings for this project/package" section have something to show
    /// immediately, without every page that wants a findings count needing its own on-demand
    /// "Scan" button and its own copy of the results.</summary>
    public IReadOnlyList<UsageResult>? LegacyFindings { get; private set; }

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
            logger.LogDebug("LoadSolutionAsync called for '{SolutionPath}' while a load is already in progress; ignoring.", solutionPath);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Starting solution load for '{SolutionPath}'.", solutionPath);

        IsLoading = true;
        LastError = null;
        NotifyChanged();

        var progress = new ActionProgress<string>(message =>
        {
            LoadingStageMessage = message;
            NotifyChanged();
        });

        try
        {
            progress.Report("Starting...");
            var solution = await parser.ParseAsync(solutionPath, progress, cancellationToken);

            var usageResults = await usageScanner.ScanAsync(solution, progress, cancellationToken);

            var legacyFindings = await legacyPatternScanner.ScanAsync(solution, progress, cancellationToken);

            Solution = solution;
            _graphCache = null; // rebuilt lazily on first access against the new Solution
            UsageResults = usageResults;
            LegacyFindings = legacyFindings;
            LastLoadedPath = solutionPath;
            logger.LogDebug("Loaded state assigned for '{SolutionPath}'; starting file watcher.", solutionPath);
            StartWatching(solution.Path);

            // Best-effort: a failure to persist "recently opened" should never fail the load
            // itself (the solution is fully loaded and usable either way).
            try
            {
                await recentWorkspacesStore.RecordOpenedAsync(solutionPath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record '{SolutionPath}' as a recently-opened workspace.", solutionPath);
            }

            stopwatch.Stop();
            logger.LogInformation(
                "Completed solution load for '{SolutionPath}' with {ProjectCount} projects in {ElapsedMs}ms.",
                solutionPath, solution.Projects.Count, stopwatch.ElapsedMilliseconds);
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

    /// <summary>Returns to the "no workspace open" state (the landing/recent-workspaces screen),
    /// without forgetting this path from the recent-workspaces list - the assessor can always
    /// reopen it from there. Stops the file watcher too, since there is no longer a loaded
    /// solution for it to trigger a re-load of.</summary>
    public void CloseSolution()
    {
        logger.LogInformation("Closing solution '{SolutionPath}'.", LastLoadedPath);
        StopWatching();
        Solution = null;
        _graphCache = null;
        UsageResults = null;
        LegacyFindings = null;
        LastLoadedPath = null;
        LastError = null;
        NotifyChanged();
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

        logger.LogDebug("Started file watcher rooted at '{Root}'.", root);
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

        logger.LogInformation("File watcher detected a change under '{SolutionPath}'; triggering re-scan.", LastLoadedPath);

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

    /// <summary>Minimal, deliberately non-<see cref="Progress{T}"/> <see cref="IProgress{T}"/>:
    /// invokes the callback synchronously on whatever thread calls <see cref="Report"/> (including
    /// worker threads inside <c>Parallel.ForEachAsync</c> during parsing), rather than
    /// <see cref="Progress{T}"/>'s SynchronizationContext-capturing/posting behavior, which is both
    /// unnecessary here (the callback just sets a field and raises an event; every subscriber to
    /// <see cref="Changed"/> already marshals back onto the Blazor renderer's sync context itself
    /// via <c>InvokeAsync</c>) and would add a layer of indirection that's harder to reason about.</summary>
    private sealed class ActionProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
