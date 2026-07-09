using System.Text.RegularExpressions;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Search;

/// <summary>
/// Default <see cref="IAdHocSourceSearcher"/>: a word-boundary-anchored, literal text search for
/// the caller's query across every project's source tree (excluding <c>bin</c>/<c>obj</c>), the
/// same broad-file-set/word-boundary-regex *approach* <c>UsageScanning.TextSearchUsageScanner</c>
/// uses for its own fixed-target search - reimplemented fresh here rather than reusing that type
/// directly, since it's internal and tightly coupled to its own fixed <c>UsageTarget</c> list.
/// Every result is tagged <see cref="UsageConfidence.TextMatch"/> (it is, honestly, just a text
/// search - nothing more) with <see cref="UsageResult.TargetName"/> set to the query itself.
/// </summary>
public sealed class AdHocSourceSearcher : IAdHocSourceSearcher
{
    /// <summary>Same broad set <c>TextSearchUsageScanner.SearchExtensions</c> covers - source plus
    /// the non-C# file kinds a plain text search can meaningfully look at.</summary>
    private static readonly string[] SearchExtensions = [".cs", ".razor", ".cshtml", ".config", ".json", ".xml"];

    public async Task<IReadOnlyList<UsageResult>> SearchAsync(SolutionModel solution, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var regex = BuildWordBoundaryRegex(query);
        var results = new List<UsageResult>();

        // A (file, line) result is unique regardless of which project "found" it first - guards
        // against double-reporting the same physical line if two projects' directories happen to
        // overlap (e.g. a project nested inside another's folder).
        var seen = new HashSet<(string FilePath, int LineNumber)>();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in EnumerateSearchableFiles(project.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string[] lines;
                try
                {
                    lines = await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // Unreadable file (locked, deleted mid-search, etc.) - skip rather than fail
                    // the whole search over one file.
                    continue;
                }

                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex];
                    if (!regex.IsMatch(line))
                    {
                        continue;
                    }

                    var lineNumber = lineIndex + 1;
                    if (!seen.Add((file, lineNumber)))
                    {
                        continue;
                    }

                    results.Add(new UsageResult
                    {
                        FilePath = file,
                        LineNumber = lineNumber,
                        MatchedSymbol = query,
                        Kind = UsageReferenceKind.Other,
                        ProjectPath = project.Path,
                        CodeSnippet = line.Trim(),
                        Confidence = UsageConfidence.TextMatch,
                        TargetName = query,
                    });
                }
            }
        }

        return results
            .OrderBy(r => r.FilePath, StringComparer.Ordinal)
            .ThenBy(r => r.LineNumber)
            .ToList();
    }

    private static IEnumerable<string> EnumerateSearchableFiles(string projectPath)
    {
        string? projectDir;
        try
        {
            projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return [];
        }

        if (projectDir is null || !Directory.Exists(projectDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(projectDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsUnderExcludedDirectory(f) && SearchExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    private static bool IsUnderExcludedDirectory(string filePath)
    {
        var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s =>
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static Regex BuildWordBoundaryRegex(string query) =>
        new(@"\b" + Regex.Escape(query) + @"\b", RegexOptions.CultureInvariant);
}
