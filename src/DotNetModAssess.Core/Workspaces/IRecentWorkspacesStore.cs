namespace DotNetModAssess.Core.Workspaces;

/// <summary>One previously-opened solution/project, as shown on the "recent workspaces" landing
/// screen. <see cref="Path"/> is always stored fully-qualified (see
/// <see cref="JsonFileRecentWorkspacesStore.RecordOpenedAsync"/>) so re-opening it later doesn't
/// depend on the process's current working directory matching whatever was true when it was first
/// recorded.</summary>
public sealed record RecentWorkspaceEntry(string Path, string DisplayName, DateTimeOffset LastOpenedUtc);

/// <summary>
/// Persists the list of solutions/projects a user has previously opened, across app restarts, so
/// the landing screen can offer "reopen" shortcuts instead of forcing a path to be retyped every
/// time. Deliberately decoupled from <see cref="Parsing.ISolutionParser"/>/<c>SolutionStateService</c>
/// - this store only remembers *that* a path was opened, not anything about its contents.
/// </summary>
public interface IRecentWorkspacesStore
{
    /// <summary>Most-recently-opened first. Never longer than the store's configured cap.</summary>
    Task<IReadOnlyList<RecentWorkspaceEntry>> GetRecentAsync(CancellationToken cancellationToken = default);

    /// <summary>Records <paramref name="path"/> as just-opened: inserts it at the front (deduped by
    /// path, so re-opening something already on the list moves it to the front rather than adding a
    /// second entry), then trims to the store's configured cap.</summary>
    Task RecordOpenedAsync(string path, CancellationToken cancellationToken = default);
}
