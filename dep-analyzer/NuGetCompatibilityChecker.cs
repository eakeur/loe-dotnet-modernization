using System.Collections.Concurrent;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DepAnalyzer;

public class NuGetCompatibilityChecker(string? solutionDir = null, string? explicitFeedUrl = null)
{
    private static readonly NuGetFramework Net8 = NuGetFramework.Parse("net8.0");
    private readonly SemaphoreSlim _semaphore = new(10);

    public async Task EnrichAsync(List<DependencyRow> rows, CancellationToken ct)
    {
        var packageRows = rows.Where(r => r.Type == "NuGetPackage").ToList();
        var withVersion = packageRows.Where(r => !string.IsNullOrEmpty(r.Version)).ToList();
        var skipped = packageRows.Count - withVersion.Count;

        if (skipped > 0)
            Console.Error.WriteLine($"Warning: {skipped} package(s) have no version and will be skipped.");

        if (withVersion.Count == 0)
        {
            Console.Error.WriteLine("No packages with versions found — skipping .NET 8 compatibility check.");
            return;
        }

        var repositories = BuildRepositories();
        Console.Error.WriteLine($"Checking .NET 8 compatibility for {withVersion.Count} package(s) across {repositories.Count} source(s)...");

        using var cache = new SourceCacheContext();
        var resultCache = new ConcurrentDictionary<string, (bool? SupportsNet8, string Frameworks)>(StringComparer.OrdinalIgnoreCase);

        int compatible = 0, incompatible = 0, unknown = 0, notFound = 0;

        var tasks = withVersion.Select(async row =>
        {
            if (!NuGetVersion.TryParse(row.Version, out var nugetVersion))
            {
                Console.Error.WriteLine($"Warning: Cannot parse version '{row.Version}' for {row.Name} — skipping.");
                Interlocked.Increment(ref unknown);
                return;
            }

            var cacheKey = $"{row.Name}@{row.Version}";

            await _semaphore.WaitAsync(ct);
            try
            {
                if (resultCache.TryGetValue(cacheKey, out var cached))
                {
                    row.SupportsNet8 = cached.SupportsNet8;
                    row.PackageFrameworks = cached.Frameworks;
                    return;
                }

                bool? supportsNet8 = null;
                string frameworks = "";
                bool found = false;

                foreach (var repo in repositories)
                {
                    try
                    {
                        var resource = await repo.GetResourceAsync<FindPackageByIdResource>(ct);
                        var depInfo = await resource.GetDependencyInfoAsync(
                            row.Name, nugetVersion, cache, NullLogger.Instance, ct);

                        if (depInfo is not null)
                        {
                            found = true;
                            (supportsNet8, frameworks) = Analyze(depInfo);
                            break;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"Warning: Source '{repo.PackageSource.Name}' failed for {row.Name} {row.Version}: {ex.Message}");
                    }
                }

                if (!found)
                {
                    Console.Error.WriteLine($"Warning: {row.Name} {row.Version} not found in any configured source.");
                    Interlocked.Increment(ref notFound);
                }
                else if (supportsNet8 is true) Interlocked.Increment(ref compatible);
                else if (supportsNet8 is false) Interlocked.Increment(ref incompatible);
                else Interlocked.Increment(ref unknown);

                resultCache[cacheKey] = (supportsNet8, frameworks);
                row.SupportsNet8 = supportsNet8;
                row.PackageFrameworks = frameworks;
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Console.Error.WriteLine(
            $"Check complete: {compatible} compatible, {incompatible} incompatible, " +
            $"{unknown} unknown, {notFound} not found.");
    }

    private List<SourceRepository> BuildRepositories()
    {
        if (explicitFeedUrl is not null)
            return [Repository.Factory.GetCoreV3(explicitFeedUrl)];

        // Auto-discover from NuGet config files (nuget.config, ~/.nuget/NuGet.Config, etc.)
        var settings = Settings.LoadDefaultSettings(
            solutionDir ?? Directory.GetCurrentDirectory());

        var sourceProvider = new PackageSourceProvider(settings);
        var sources = sourceProvider.LoadPackageSources()
            .Where(s => s.IsEnabled)
            .ToList();

        if (sources.Count == 0)
        {
            Console.Error.WriteLine("Warning: No enabled NuGet sources found in config — falling back to nuget.org.");
            return [Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json")];
        }

        Console.Error.WriteLine($"NuGet sources: {string.Join(", ", sources.Select(s => s.Name))}");
        return sources.Select(s => Repository.Factory.GetCoreV3(s)).ToList();
    }

    // Returns (supportsNet8, semicolon-separated list of target framework monikers)
    private static (bool? SupportsNet8, string Frameworks) Analyze(FindPackageByIdDependencyInfo depInfo)
    {
        var groups = depInfo.DependencyGroups.ToList();
        if (groups.Count == 0) return (null, "");

        bool supportsNet8 = false;
        var monikers = new List<string>();

        foreach (var group in groups)
        {
            var tf = group.TargetFramework;

            string moniker;
            if (tf.IsAny || tf == NuGetFramework.AnyFramework)
            {
                moniker = "any";
                supportsNet8 = true;
            }
            else
            {
                moniker = tf.GetShortFolderName();
                if (DefaultCompatibilityProvider.Instance.IsCompatible(Net8, tf))
                    supportsNet8 = true;
            }

            if (!string.IsNullOrEmpty(moniker))
                monikers.Add(moniker);
        }

        var frameworks = string.Join(";", monikers.Order());
        return (supportsNet8, frameworks);
    }
}
