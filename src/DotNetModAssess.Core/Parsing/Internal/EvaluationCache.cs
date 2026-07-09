using System.Collections.Concurrent;
using Buildalyzer;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Core.Parsing.Internal;

/// <summary>
/// Caches <see cref="ProjectEvaluationOutcome"/> per project path, keyed also by that file's last
/// write time so a change to the .csproj (or anything MSBuild-evaluates as part of it) invalidates
/// the entry. This is what makes repeated re-scans of a live-watched solution fast: unchanged
/// projects skip actual MSBuild evaluation entirely and reuse the cached result. Process-lifetime
/// (static), since it's keyed on absolute path + write time and is safe to share across every
/// <see cref="BuildalyzerSolutionParser.ParseAsync"/> call and every Blazor circuit.
/// </summary>
internal static class EvaluationCache
{
    private sealed record Entry(DateTime LastWriteTimeUtc, ProjectEvaluationOutcome Outcome);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static ProjectEvaluationOutcome GetOrEvaluate(IAnalyzerManager manager, string projectPath, ILogger? logger = null)
    {
        var lastWriteTimeUtc = File.Exists(projectPath) ? File.GetLastWriteTimeUtc(projectPath) : DateTime.MinValue;

        if (Entries.TryGetValue(projectPath, out var cached) && cached.LastWriteTimeUtc == lastWriteTimeUtc)
        {
            logger?.LogDebug("Evaluation cache hit for project {ProjectPath}", projectPath);
            return cached.Outcome;
        }

        logger?.LogDebug("Evaluation cache miss for project {ProjectPath}; evaluating now", projectPath);
        var outcome = ProjectEvaluator.Evaluate(manager, projectPath, logger);
        Entries[projectPath] = new Entry(lastWriteTimeUtc, outcome);
        return outcome;
    }

    /// <summary>Drops every cached entry. Used when a structural change (e.g. the .sln itself) means stale entries could otherwise linger for paths no longer in scope.</summary>
    public static void Clear() => Entries.Clear();
}
