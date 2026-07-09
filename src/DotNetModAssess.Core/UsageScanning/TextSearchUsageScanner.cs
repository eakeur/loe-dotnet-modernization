using System.Text.RegularExpressions;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.UsageScanning;

/// <summary>
/// Second, independent usage-detection pass invoked by <see cref="RoslynUsageScanner.ScanAsync"/>:
/// a plain, word-boundary-aware text search for each <see cref="RoslynUsageScanner.UsageTarget"/>'s
/// literal name, across a broader file set than the Roslyn syntax pass.
///
/// <para>
/// WHY: the Roslyn pass only understands ".cs" syntax it can classify (using directives,
/// fully-qualified type references, member accesses, attributes, base lists). It structurally
/// cannot see: usages inside non-C# files (".razor"/".cshtml"/config/JSON/XML), or a target's name
/// appearing as plain text that isn't itself a recognized syntax position (e.g. inside a string
/// literal passed to a reflection call like <c>Type.GetType("Some.Namespace.Type")</c>, or in a
/// comment). This pass catches those by searching literal text instead of parsing syntax, at the
/// cost of more false positives (comments, string literals, coincidental name collisions) - an
/// accepted, documented tradeoff for a risk-assessment tool: overreporting "possible" usages for
/// manual triage is safer than silently under-reporting.
/// </para>
///
/// <para>
/// NOT SOLVED BY THIS PASS: bare/unqualified identifier resolution in general. E.g. given
/// <c>using System.Web;</c> followed later by a bare <c>HttpContext.Current</c> reference (no
/// further qualification), a search for the literal string "System.Web" still will not match that
/// later line, because the string "System.Web" never appears there - telling "HttpContext" apart
/// from an unrelated same-named symbol requires full compilation/symbol binding, which is out of
/// scope for both passes. This pass only helps when the target's own literal name text appears
/// somewhere the Roslyn structural scan doesn't look.
/// </para>
///
/// <para>
/// De-duplication: every result found here is checked against the Roslyn pass's results by the
/// exact triple (FilePath, LineNumber, MatchedSymbol); a match already confirmed by Roslyn at that
/// triple is not re-reported here as a separate <see cref="UsageConfidence.TextMatch"/> result -
/// the goal is to surface what Roslyn didn't already confirm, not to double-count.
/// </para>
/// </summary>
internal static class TextSearchUsageScanner
{
    /// <summary>File extensions searched, beyond what the Roslyn pass already covers (".cs" is
    /// included too, since string literals/comments inside ".cs" files are themselves invisible to
    /// the Roslyn structural scan).</summary>
    private static readonly string[] SearchExtensions = [".cs", ".razor", ".cshtml", ".config", ".json", ".xml"];

    public static async Task<IReadOnlyList<UsageResult>> ScanAsync(
        SolutionModel solution,
        IReadOnlyList<RoslynUsageScanner.UsageTarget> targets,
        IReadOnlyList<UsageResult> confirmedResults,
        CancellationToken cancellationToken)
    {
        var confirmedKeys = new HashSet<(string FilePath, int LineNumber, string MatchedSymbol)>(
            confirmedResults.Select(r => (r.FilePath, r.LineNumber, r.MatchedSymbol)));

        var results = new List<UsageResult>();
        var seenKeys = new HashSet<(string FilePath, int LineNumber, string MatchedSymbol)>();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectTargets = RoslynUsageScanner.GetTargetsForProject(targets, project);
            if (projectTargets.Count == 0)
            {
                continue;
            }

            var patterns = projectTargets
                .Select(t => (t.Name, Regex: BuildWordBoundaryRegex(t.Name)))
                .ToList();

            foreach (var file in RoslynUsageScanner.EnumerateFilesForTextSearch(project, SearchExtensions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string[] lines;
                try
                {
                    lines = await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // Unreadable file (locked, deleted mid-scan, etc.) -- skip rather than fail the
                    // whole scan over one file this pass doesn't strictly need.
                    continue;
                }

                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex];
                    var lineNumber = lineIndex + 1;

                    foreach (var (name, regex) in patterns)
                    {
                        if (!regex.IsMatch(line))
                        {
                            continue;
                        }

                        var key = (file, lineNumber, name);
                        if (confirmedKeys.Contains(key) || !seenKeys.Add(key))
                        {
                            continue;
                        }

                        results.Add(new UsageResult
                        {
                            FilePath = file,
                            LineNumber = lineNumber,
                            MatchedSymbol = name,
                            Kind = UsageReferenceKind.Other,
                            ProjectPath = project.Path,
                            CodeSnippet = line.Trim(),
                            Confidence = UsageConfidence.TextMatch,
                        });
                    }
                }
            }
        }

        return results
            .OrderBy(r => r.FilePath, StringComparer.Ordinal)
            .ThenBy(r => r.LineNumber)
            .ThenBy(r => r.MatchedSymbol, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Builds a `\b`-anchored regex for a target name, escaping any regex metacharacters
    /// (dots being the common one in namespace/package names like "System.Web") so the target is
    /// matched literally, with a word boundary on each side.</summary>
    private static Regex BuildWordBoundaryRegex(string targetName) =>
        new(@"\b" + Regex.Escape(targetName) + @"\b", RegexOptions.CultureInvariant);
}
