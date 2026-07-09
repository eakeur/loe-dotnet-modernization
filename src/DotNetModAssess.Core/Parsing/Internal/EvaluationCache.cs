using System.Collections.Concurrent;
using Buildalyzer;

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

    public static ProjectEvaluationOutcome GetOrEvaluate(IAnalyzerManager manager, string projectPath)
    {
        var lastWriteTimeUtc = File.Exists(projectPath) ? File.GetLastWriteTimeUtc(projectPath) : DateTime.MinValue;

        if (Entries.TryGetValue(projectPath, out var cached) && cached.LastWriteTimeUtc == lastWriteTimeUtc)
        {
            return cached.Outcome;
        }

        var outcome = ProjectEvaluator.Evaluate(manager, projectPath);
        Entries[projectPath] = new Entry(lastWriteTimeUtc, outcome);
        return outcome;
    }

    /// <summary>Drops every cached entry. Used when a structural change (e.g. the .sln itself) means stale entries could otherwise linger for paths no longer in scope.</summary>
    public static void Clear() => Entries.Clear();
}
