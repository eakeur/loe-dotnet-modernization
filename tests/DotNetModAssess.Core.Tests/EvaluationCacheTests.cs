using Buildalyzer;
using DotNetModAssess.Core.Parsing.Internal;

namespace DotNetModAssess.Core.Tests;

/// <summary>
/// Exercises <see cref="EvaluationCache"/> directly (via InternalsVisibleTo) against a throwaway
/// project file, independent of the shared SampleLegacySolution fixture other tests use - each
/// test here creates and deletes its own temp .csproj so cache entries from other tests never
/// interfere (the cache is process-lifetime/static, keyed by absolute path).
/// </summary>
public class EvaluationCacheTests
{
    private static string CreateTempProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EvaluationCacheTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Temp.csproj");
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        return path;
    }

    [Fact]
    public void GetOrEvaluate_ReturnsSameCachedInstance_WhenFileUnchanged()
    {
        var path = CreateTempProject();
        try
        {
            MSBuildEnvironmentInitializer.EnsureRegistered();
            var manager = new AnalyzerManager();

            var first = EvaluationCache.GetOrEvaluate(manager, path);
            var second = EvaluationCache.GetOrEvaluate(manager, path);

            Assert.Same(first, second);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void GetOrEvaluate_ReEvaluates_WhenFileWriteTimeChanges()
    {
        var path = CreateTempProject();
        try
        {
            MSBuildEnvironmentInitializer.EnsureRegistered();
            var manager = new AnalyzerManager();

            var first = EvaluationCache.GetOrEvaluate(manager, path);

            // Bump the write time without changing content - the cache keys on write time, not a
            // content hash, matching the "file path + last-write-time" invalidation the project's
            // own spec calls for.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

            var second = EvaluationCache.GetOrEvaluate(manager, path);

            Assert.NotSame(first, second);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
