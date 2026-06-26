using System.Collections.Concurrent;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DepAnalyzer;

public class NuGetCompatibilityChecker(string feedUrl = "https://api.nuget.org/v3/index.json")
{
    private static readonly NuGetFramework Net8 = NuGetFramework.Parse("net8.0");
    private readonly SemaphoreSlim _semaphore = new(10);

    public async Task EnrichAsync(List<DependencyRow> rows, CancellationToken ct)
    {
        var packageRows = rows
            .Where(r => r.Type == "NuGetPackage")
            .ToList();

        var withVersion = packageRows.Where(r => !string.IsNullOrEmpty(r.Version)).ToList();
        var withoutVersion = packageRows.Count - withVersion.Count;

        if (withoutVersion > 0)
            Console.Error.WriteLine($"Warning: {withoutVersion} package(s) have no version and will be skipped for .NET 8 check.");

        if (withVersion.Count == 0)
        {
            Console.Error.WriteLine("No packages with versions found. Skipping .NET 8 check.");
            return;
        }

        Console.Error.WriteLine($"Checking .NET 8 compatibility for {withVersion.Count} packages via {feedUrl}...");

        var repository = Repository.Factory.GetCoreV3(feedUrl);
        using var cache = new SourceCacheContext();

        FindPackageByIdResource? resource = null;
        try
        {
            resource = await repository.GetResourceAsync<FindPackageByIdResource>(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to connect to NuGet feed {feedUrl}: {ex.Message}");
            return;
        }

        // ConcurrentDictionary: safe for concurrent reads and writes from multiple tasks
        var resultCache = new ConcurrentDictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

        var tasks = withVersion.Select(async row =>
        {
            var cacheKey = $"{row.Name}@{row.Version}";

            await _semaphore.WaitAsync(ct);
            try
            {
                if (resultCache.TryGetValue(cacheKey, out var cached))
                {
                    row.SupportsNet8 = cached;
                    return;
                }

                var result = await CheckPackageAsync(resource, cache, row.Name, row.Version, ct);
                resultCache[cacheKey] = result;
                row.SupportsNet8 = result;
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        Console.Error.WriteLine("NuGet compatibility check complete.");
    }

    private static async Task<bool?> CheckPackageAsync(
        FindPackageByIdResource resource,
        SourceCacheContext cache,
        string id,
        string version,
        CancellationToken ct)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
            return null;

        try
        {
            var depInfo = await resource.GetDependencyInfoAsync(id, nugetVersion, cache, NullLogger.Instance, ct);
            if (depInfo == null) return null;

            var groups = depInfo.DependencyGroups.ToList();

            // No dependency groups: package has no declared framework constraints — treat as unknown
            if (groups.Count == 0) return null;

            foreach (var group in groups)
            {
                var tf = group.TargetFramework;

                // "Any" framework means it works everywhere
                if (tf.IsAny || tf == NuGetFramework.AnyFramework)
                    return true;

                if (DefaultCompatibilityProvider.Instance.IsCompatible(Net8, tf))
                    return true;
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to check {id} {version}: {ex.Message}");
            return null;
        }
    }
}
