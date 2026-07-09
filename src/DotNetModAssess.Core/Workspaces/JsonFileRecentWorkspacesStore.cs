using System.Text.Json;

namespace DotNetModAssess.Core.Workspaces;

/// <summary>
/// <see cref="IRecentWorkspacesStore"/> backed by a small JSON file on disk, so the "recent
/// workspaces" list survives app restarts without needing any real database. Cross-platform
/// default location is <c>%LocalAppData%/DotNetModAssess/recent-workspaces.json</c> (Windows) or the
/// XDG/HOME equivalent .NET's <see cref="Environment.SpecialFolder.LocalApplicationData"/> resolves
/// to on Linux/macOS - callers (tests, in particular) can override <see cref="_filePath"/> via the
/// constructor to point at an isolated temp file instead.
/// </summary>
public sealed class JsonFileRecentWorkspacesStore : IRecentWorkspacesStore
{
    private const int MaxEntries = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Serializes reads/writes against this store instance so two concurrent callers
    /// (e.g. two Blazor circuits loading solutions at the same time) don't race on a
    /// read-modify-write of the same file. Does not protect against a second *process* writing the
    /// same file concurrently - not a scenario this single-desktop-app tool needs to handle.</summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly string _filePath;

    public JsonFileRecentWorkspacesStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotNetModAssess",
            "recent-workspaces.json");
    }

    public async Task<IReadOnlyList<RecentWorkspaceEntry>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordOpenedAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadAllAsync(cancellationToken).ConfigureAwait(false);

            var deduped = entries
                .Where(e => !string.Equals(e.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            deduped.Insert(0, new RecentWorkspaceEntry(fullPath, Path.GetFileName(fullPath), DateTimeOffset.UtcNow));

            await WriteAllAsync(deduped.Take(MaxEntries).ToList(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<RecentWorkspaceEntry>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            var entries = await JsonSerializer.DeserializeAsync<List<RecentWorkspaceEntry>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return entries ?? [];
        }
        catch (JsonException)
        {
            // Corrupt/partially-written file (e.g. process killed mid-write) - treat as empty
            // rather than throwing away the assessor's ability to open anything.
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private async Task WriteAllAsync(List<RecentWorkspaceEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write-to-temp-then-move so a crash mid-write never leaves a half-written, unparsable
        // JSON file behind for the next ReadAllAsync to choke on.
        var tempPath = _filePath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
