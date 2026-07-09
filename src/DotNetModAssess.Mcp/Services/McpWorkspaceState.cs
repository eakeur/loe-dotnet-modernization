using System.Diagnostics;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.UsageScanning;
using DotNetModAssess.Core.Workspaces;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Mcp.Services;

/// <summary>Coarse-grained state of the most recent (or in-progress) load attempt, surfaced to
/// agents via the <c>get_load_status</c> tool.</summary>
public enum WorkspaceLoadStatus
{
    /// <summary>The very first load hasn't been kicked off yet (should never actually be
    /// observable by a tool in practice - <see cref="StartAsync"/> is called synchronously before
    /// the MCP transport starts accepting connections - but kept as a distinct value rather than
    /// conflating it with <see cref="Loading"/> for clarity).</summary>
    NotStarted,
    Loading,
    Loaded,
    Failed
}

/// <summary>
/// Process-wide, singleton cousin of <c>DotNetModAssess.Web.Services.SolutionStateService</c> - see
/// that class's own doc comment for the general shape (real <see cref="IProgress{T}"/>-driven
/// progress reporting, lazily-built <see cref="Graph"/>, eager usage + legacy-pattern scanning
/// alongside parsing, debounced <see cref="FileSystemWatcher"/>-driven live reload).
///
/// Differences from <c>SolutionStateService</c>, driven by this being a long-running MCP server
/// process rather than a Blazor circuit:
/// <list type="bullet">
/// <item>Singleton for the whole process lifetime - there is exactly one loaded workspace per
/// server instance, fixed at startup via the required command-line path argument.</item>
/// <item>No <c>Changed</c> event - nothing here renders UI. Instead, every load attempt (the
/// initial one, every file-watcher-triggered reload, and every explicit <c>reload</c> tool call)
/// is exposed via <see cref="LoadingTask"/>, a <see cref="Task"/> that callers can <c>await</c> to
/// block until the most recently started load finishes (successfully or not) - this is what lets
/// every data-returning tool "transparently wait until ready" with no bespoke retry logic: it just
/// does <c>await state.LoadingTask;</c> before touching <see cref="Solution"/>/<see cref="Graph"/>/etc.
/// <see cref="LoadingTask"/> itself never faults - any failure during loading is captured in
/// <see cref="Status"/>/<see cref="LastError"/> instead, so awaiting it is always safe.</item>
/// <item><see cref="GetLoadStatusSnapshot"/> is the one exception: it reads current fields directly
/// without awaiting anything, so the dedicated <c>get_load_status</c> tool can report progress on a
/// large solution instead of an agent blocking blindly on a data tool.</item>
/// </list>
/// </summary>
public sealed class McpWorkspaceState(
    ISolutionParser parser,
    IDependencyGraphBuilder graphBuilder,
    IUsageScanner usageScanner,
    ILegacyPatternScanner legacyPatternScanner,
    IRecentWorkspacesStore recentWorkspacesStore,
    ILogger<McpWorkspaceState> logger) : IDisposable
{
    private static readonly string[] WatchedFilters =
    [
        "*.sln", "*.slnf", "*.csproj",
        "Directory.Build.props", "Directory.Build.targets",
        "Directory.Packages.props", "NuGet.Config"
    ];

    private const int DebounceMs = 500;

    private readonly object _loadGate = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    private DependencyGraph? _graphCache;

    private string? _configuredSolutionPath;

    public SolutionModel? Solution { get; private set; }

    /// <summary>Lazily built from <see cref="Solution"/> on first access, then cached until the
    /// next load invalidates it - see <c>SolutionStateService.Graph</c> for why laziness here costs
    /// nothing despite the build itself being cheap.</summary>
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

    public IReadOnlyList<UsageResult>? LegacyFindings { get; private set; }

    public WorkspaceLoadStatus Status { get; private set; } = WorkspaceLoadStatus.NotStarted;

    public string? LoadingStageMessage { get; private set; }

    public string? LastLoadedPath { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? LastLoadCompletedUtc { get; private set; }

    public bool IsWatching => _watcher is not null;

    /// <summary>
    /// The most recently started load attempt. Every data tool should <c>await</c> this before
    /// touching <see cref="Solution"/>/<see cref="Graph"/>/<see cref="UsageResults"/>/
    /// <see cref="LegacyFindings"/>: if loading already finished by the time a tool is invoked
    /// (the common case), this returns instantly; if invoked too early, it transparently waits.
    /// Deliberately never faults - see this class's own doc comment.
    /// </summary>
    public Task LoadingTask { get; private set; } = Task.CompletedTask;

    /// <summary>Kicks off the very first load for <paramref name="solutionPath"/>, remembering it
    /// so a later parameterless <see cref="ReloadAsync"/> (and the file watcher) knows what to
    /// re-load. Must be called exactly once, synchronously, before the MCP transport starts
    /// accepting connections - see <see cref="Program"/>.</summary>
    public Task StartAsync(string solutionPath)
    {
        _configuredSolutionPath = solutionPath;
        return TriggerLoad(solutionPath, CancellationToken.None);
    }

    /// <summary>Explicit manual re-trigger of the load for the already-configured solution path -
    /// the <c>reload</c> tool's fast-path fallback to the automatic file-watcher-driven reload.</summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_configuredSolutionPath is null)
        {
            throw new InvalidOperationException($"{nameof(StartAsync)} must be called before {nameof(ReloadAsync)}.");
        }

        return TriggerLoad(_configuredSolutionPath, cancellationToken);
    }

    /// <summary>Synchronous, non-<c>async</c> gate: returns before any awaiting happens, so
    /// <see cref="LoadingTask"/> is guaranteed to be assigned to the new attempt's task (or, if one
    /// is already in flight, joined to it) by the time this method returns - callers that fire this
    /// off without awaiting (<see cref="Program"/>'s background kick-off) can rely on
    /// <see cref="LoadingTask"/> being correct immediately afterwards, with no race.</summary>
    private Task TriggerLoad(string solutionPath, CancellationToken cancellationToken)
    {
        lock (_loadGate)
        {
            if (Status == WorkspaceLoadStatus.Loading)
            {
                // A load is already in flight (e.g. the file watcher fired again while the
                // previous debounce-triggered reload was still running, or a manual `reload` tool
                // call raced with it) - join the existing attempt rather than starting a second,
                // overlapping one that would stomp the same mutable fields.
                logger.LogDebug("Load already in progress for '{SolutionPath}'; joining existing attempt.", solutionPath);
                return LoadingTask;
            }

            Status = WorkspaceLoadStatus.Loading;
            LastError = null;
            LoadingStageMessage = "Starting...";

            var task = RunLoadAsync(solutionPath, cancellationToken);
            LoadingTask = task;
            return task;
        }
    }

    private async Task RunLoadAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Starting workspace load for '{SolutionPath}'.", solutionPath);

        var progress = new ActionProgress<string>(message =>
        {
            LoadingStageMessage = message;
            logger.LogDebug("Load progress for '{SolutionPath}': {StageMessage}", solutionPath, message);
        });

        try
        {
            progress.Report("Starting...");
            var solution = await parser.ParseAsync(solutionPath, progress, cancellationToken).ConfigureAwait(false);

            var usageResults = await usageScanner.ScanAsync(solution, progress, cancellationToken).ConfigureAwait(false);

            var legacyFindings = await legacyPatternScanner.ScanAsync(solution, progress, cancellationToken).ConfigureAwait(false);

            Solution = solution;
            _graphCache = null; // rebuilt lazily on first access against the new Solution
            UsageResults = usageResults;
            LegacyFindings = legacyFindings;
            LastLoadedPath = solutionPath;
            StartWatching(solution.Path);

            // Best-effort: a failure to persist "recently opened" should never fail the load
            // itself (the solution is fully loaded and usable either way).
            try
            {
                await recentWorkspacesStore.RecordOpenedAsync(solutionPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record '{SolutionPath}' as a recently-opened workspace.", solutionPath);
            }

            Status = WorkspaceLoadStatus.Loaded;

            stopwatch.Stop();
            logger.LogInformation(
                "Completed workspace load for '{SolutionPath}' with {ProjectCount} projects in {ElapsedMs}ms.",
                solutionPath, solution.Projects.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load solution from '{SolutionPath}'.", solutionPath);
            LastError = ex.Message;
            Status = WorkspaceLoadStatus.Failed;
        }
        finally
        {
            LoadingStageMessage = null;
            LastLoadCompletedUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Point-in-time snapshot for the <c>get_load_status</c> tool. Deliberately reads
    /// fields directly with no locking/awaiting - a torn read of a couple of independent fields
    /// mid-load is a non-issue for a progress-status tool (the next poll will just see the fully
    /// updated state), and never blocking here is the entire point.</summary>
    public LoadStatusSnapshot GetLoadStatusSnapshot() => new(
        Status,
        LoadingStageMessage,
        LastLoadedPath,
        LastError,
        IsWatching,
        LastLoadCompletedUtc,
        Solution?.Projects.Count,
        UsageResults?.Count,
        LegacyFindings?.Count);

    /// <summary>
    /// (Re)starts watching the directory tree rooted at the loaded solution's directory. Safe to
    /// call repeatedly - any previous watcher is torn down first. Mirrors
    /// <c>SolutionStateService.StartWatching</c> exactly.
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
        // Editors/IDEs frequently emit several change notifications for a single logical save;
        // debounce by restarting the timer on every event and only acting once the filesystem has
        // been quiet for DebounceMs.
        _debounceTimer?.Change(DebounceMs, Timeout.Infinite);

    private void OnDebounceElapsed()
    {
        if (LastLoadedPath is null)
        {
            return;
        }

        logger.LogInformation("File watcher detected a change under '{SolutionPath}'; triggering re-scan.", LastLoadedPath);

        // Timer callbacks run on a thread-pool thread; TriggerLoad only touches this singleton's
        // own state (no UI/circuit to marshal back onto), so no extra dispatch is needed here.
        _ = TriggerLoad(LastLoadedPath, CancellationToken.None);
    }

    public void Dispose()
    {
        StopWatching();
        _debounceTimer?.Dispose();
    }

    /// <summary>Minimal, deliberately non-<see cref="Progress{T}"/> <see cref="IProgress{T}"/>:
    /// invokes the callback synchronously on whatever thread calls <see cref="Report"/>, exactly
    /// like <c>SolutionStateService.ActionProgress</c> - see that class for the rationale.</summary>
    private sealed class ActionProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}

/// <summary>Plain, MCP-serializable snapshot of <see cref="McpWorkspaceState"/>'s current loading
/// progress/result, returned by the <c>get_load_status</c> tool.</summary>
public sealed record LoadStatusSnapshot(
    WorkspaceLoadStatus Status,
    string? StageMessage,
    string? LoadedPath,
    string? LastError,
    bool IsWatching,
    DateTimeOffset? LastLoadCompletedUtc,
    int? ProjectCount,
    int? UsageResultCount,
    int? LegacyFindingCount);
