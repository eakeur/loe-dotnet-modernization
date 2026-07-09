using DotNetModAssess.Core.Workspaces;

namespace DotNetModAssess.Core.Tests;

/// <summary>
/// Exercises <see cref="JsonFileRecentWorkspacesStore"/> against a throwaway temp file per test
/// (same isolation approach as <see cref="EvaluationCacheTests"/>'s <c>CreateTempProject</c>) so
/// tests never touch the real user profile's <c>recent-workspaces.json</c> or leak state between
/// runs.
/// </summary>
public class JsonFileRecentWorkspacesStoreTests
{
    private static string CreateTempFilePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RecentWorkspacesTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "recent-workspaces.json");
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsEmpty_WhenNoFileExistsYet()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new JsonFileRecentWorkspacesStore(path);

            var result = await store.GetRecentAsync();

            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task RecordOpenedAsync_PersistsAcrossStoreInstances()
    {
        var path = CreateTempFilePath();
        try
        {
            var tempProjectDir = Path.Combine(Path.GetDirectoryName(path)!, "solution");
            Directory.CreateDirectory(tempProjectDir);
            var solutionPath = Path.Combine(tempProjectDir, "MySolution.sln");
            File.WriteAllText(solutionPath, string.Empty);

            var store = new JsonFileRecentWorkspacesStore(path);
            await store.RecordOpenedAsync(solutionPath);

            // A brand-new store instance pointed at the same file should see the persisted entry -
            // this is the whole point of the store surviving app restarts.
            var reopenedStore = new JsonFileRecentWorkspacesStore(path);
            var result = await reopenedStore.GetRecentAsync();

            Assert.Single(result);
            Assert.Equal(Path.GetFullPath(solutionPath), result[0].Path);
            Assert.Equal("MySolution.sln", result[0].DisplayName);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task RecordOpenedAsync_MovesExistingEntryToFront_InsteadOfDuplicating()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new JsonFileRecentWorkspacesStore(path);

            await store.RecordOpenedAsync("/repo/First.sln");
            await store.RecordOpenedAsync("/repo/Second.sln");
            await store.RecordOpenedAsync("/repo/First.sln");

            var result = await store.GetRecentAsync();

            Assert.Equal(2, result.Count);
            Assert.Equal(Path.GetFullPath("/repo/First.sln"), result[0].Path);
            Assert.Equal(Path.GetFullPath("/repo/Second.sln"), result[1].Path);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task RecordOpenedAsync_CapsAtTenEntries_MostRecentFirst()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new JsonFileRecentWorkspacesStore(path);

            for (var i = 0; i < 12; i++)
            {
                await store.RecordOpenedAsync($"/repo/Solution{i}.sln");
            }

            var result = await store.GetRecentAsync();

            Assert.Equal(10, result.Count);
            Assert.Equal(Path.GetFullPath("/repo/Solution11.sln"), result[0].Path);
            Assert.Equal(Path.GetFullPath("/repo/Solution2.sln"), result[^1].Path);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsEmpty_WhenFileIsCorrupt()
    {
        var path = CreateTempFilePath();
        try
        {
            await File.WriteAllTextAsync(path, "{ not valid json ][");

            var store = new JsonFileRecentWorkspacesStore(path);
            var result = await store.GetRecentAsync();

            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
