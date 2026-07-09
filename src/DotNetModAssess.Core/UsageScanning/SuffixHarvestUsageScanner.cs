using System.Text.RegularExpressions;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.UsageScanning;

/// <summary>
/// Third, independent usage-detection pass invoked by <see cref="RoslynUsageScanner.ScanAsync"/>:
/// harvests a per-target vocabulary of progressively-shortened suffixes from the Roslyn pass's own
/// <see cref="UsageConfidence.Confirmed"/> results, then searches for those too.
///
/// <para>
/// WHY: neither the Roslyn pass nor <see cref="TextSearchUsageScanner"/> can see a bare/unqualified
/// identifier reference - e.g. given <c>using System.Web;</c> followed later by a bare
/// <c>HttpContext.Current</c> (no further qualification), Roslyn cannot structurally tell
/// "HttpContext" apart from an unrelated same-named symbol without full symbol binding, and a text
/// search for the literal target name "System.Web" never matches a line that doesn't contain that
/// text. But if *elsewhere in the same solution* Roslyn already confirmed a fully-qualified
/// reference like "System.Web.HttpContext" for that same target, that tells us "HttpContext" is a
/// real type belonging to "System.Web" in this specific solution - so it's worth searching the
/// whole codebase for the bare text "HttpContext" (and any intermediate qualified forms) too. That
/// harvest-and-search is exactly what this pass does.
/// </para>
///
/// <para>
/// HARVESTING: for every <see cref="UsageConfidence.Confirmed"/> result whose <see cref="UsageReferenceKind"/>
/// is <see cref="UsageReferenceKind.TypeReference"/>, <see cref="UsageReferenceKind.MemberAccess"/>,
/// <see cref="UsageReferenceKind.Attribute"/>, or <see cref="UsageReferenceKind.BaseTypeOrInterface"/>
/// (deliberately not <see cref="UsageReferenceKind.UsingDirective"/> - a using directive's
/// <c>MatchedSymbol</c> is just the target's own namespace path, nothing extra to harvest from it),
/// its <c>MatchedSymbol</c> (by construction from <c>RoslynUsageScanner.MatchTargetPrefix</c>, always
/// the target's name plus one more segment, e.g. "System.Web.HttpContext" for target "System.Web")
/// is split into dot-separated segments, and every suffix shorter than the full string is generated
/// by dropping segments from the left - e.g. "Web.HttpContext" and "HttpContext" for that example.
/// Suffix generation is capped at <see cref="MaxHarvestedSuffixesPerIdentifier"/> per harvested
/// identifier (a compute bound on unusually deep qualified names, not a precision filter - the
/// single-segment bare suffix is never skipped, since that's specifically the case this pass exists
/// to catch). Harvested suffixes are deduplicated per target.
/// </para>
///
/// <para>
/// SEARCH: for each target, every harvested suffix (not the target's own base name - that's
/// <see cref="TextSearchUsageScanner"/>'s job, not duplicated here) is searched with the same
/// word-boundary-regex approach across the same broad file set <see cref="TextSearchUsageScanner"/>
/// already covers. Results are tagged <see cref="UsageConfidence.SuffixMatch"/> - the weakest of the
/// three tiers, since a short/common harvested suffix (a type named "Client" or "Manager") is a
/// high-false-positive search term. <c>MatchedSymbol</c> is set to the actual harvested suffix text
/// found (e.g. "HttpContext"); <see cref="UsageResult.TargetName"/> is set to the *originating*
/// target's name, not the harvested text.
/// </para>
///
/// <para>
/// De-duplication: a (FilePath, LineNumber, TargetName) triple already covered by a
/// <see cref="UsageConfidence.Confirmed"/> or <see cref="UsageConfidence.TextMatch"/> result (passed
/// in as <c>alreadyReportedResults</c>) never gets a SuffixMatch result too. Within this pass alone,
/// at most one SuffixMatch result is emitted per (FilePath, LineNumber, TargetName) even when
/// several harvested suffixes for the same target match the same line - the longest (most specific)
/// matching suffix wins.
/// </para>
/// </summary>
internal static class SuffixHarvestUsageScanner
{
    /// <summary>Compute bound on how many progressively-shorter suffixes are generated per
    /// harvested confirmed identifier - not a precision filter (see class docs).</summary>
    private const int MaxHarvestedSuffixesPerIdentifier = 4;

    private static readonly UsageReferenceKind[] HarvestableKinds =
    [
        UsageReferenceKind.TypeReference,
        UsageReferenceKind.MemberAccess,
        UsageReferenceKind.Attribute,
        UsageReferenceKind.BaseTypeOrInterface,
    ];

    public static async Task<IReadOnlyList<UsageResult>> ScanAsync(
        SolutionModel solution,
        IReadOnlyList<RoslynUsageScanner.UsageTarget> targets,
        IReadOnlyList<UsageResult> confirmedResults,
        IReadOnlyList<UsageResult> alreadyReportedResults,
        CancellationToken cancellationToken)
    {
        var suffixesByTarget = HarvestSuffixes(confirmedResults);
        if (suffixesByTarget.Count == 0)
        {
            return [];
        }

        var coveredKeys = new HashSet<(string FilePath, int LineNumber, string TargetName)>(
            alreadyReportedResults.Select(r => (r.FilePath, r.LineNumber, r.TargetName)));

        var results = new List<UsageResult>();
        var seenKeys = new HashSet<(string FilePath, int LineNumber, string TargetName)>();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectTargets = RoslynUsageScanner.GetTargetsForProject(targets, project);
            if (projectTargets.Count == 0)
            {
                continue;
            }

            var patterns = projectTargets
                .Where(t => suffixesByTarget.ContainsKey(t.Name))
                .SelectMany(t => suffixesByTarget[t.Name].Select(suffix =>
                    (TargetName: t.Name, Suffix: suffix, Regex: TextSearchUsageScanner.BuildWordBoundaryRegex(suffix))))
                .ToList();

            if (patterns.Count == 0)
            {
                continue;
            }

            foreach (var file in RoslynUsageScanner.EnumerateFilesForTextSearch(project, TextSearchUsageScanner.SearchExtensions))
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

                    // One file read, one pass over every applicable target/suffix regex against
                    // that single in-memory line -- no repeated file I/O per search term.
                    Dictionary<string, string>? bestSuffixPerTarget = null;
                    foreach (var (targetName, suffix, regex) in patterns)
                    {
                        if (!regex.IsMatch(line))
                        {
                            continue;
                        }

                        bestSuffixPerTarget ??= new Dictionary<string, string>(StringComparer.Ordinal);
                        if (!bestSuffixPerTarget.TryGetValue(targetName, out var current) || suffix.Length > current.Length)
                        {
                            bestSuffixPerTarget[targetName] = suffix;
                        }
                    }

                    if (bestSuffixPerTarget is null)
                    {
                        continue;
                    }

                    foreach (var (targetName, bestSuffix) in bestSuffixPerTarget)
                    {
                        var key = (file, lineNumber, targetName);
                        if (coveredKeys.Contains(key) || !seenKeys.Add(key))
                        {
                            continue;
                        }

                        results.Add(new UsageResult
                        {
                            FilePath = file,
                            LineNumber = lineNumber,
                            MatchedSymbol = bestSuffix,
                            Kind = UsageReferenceKind.Other,
                            ProjectPath = project.Path,
                            CodeSnippet = line.Trim(),
                            Confidence = UsageConfidence.SuffixMatch,
                            TargetName = targetName,
                        });
                    }
                }
            }
        }

        return results
            .OrderBy(r => r.FilePath, StringComparer.Ordinal)
            .ThenBy(r => r.LineNumber)
            .ThenBy(r => r.TargetName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Builds the per-target harvested-suffix vocabulary from the Roslyn pass's confirmed
    /// results (see class docs for the harvesting rule and the cap).</summary>
    private static Dictionary<string, HashSet<string>> HarvestSuffixes(IReadOnlyList<UsageResult> confirmedResults)
    {
        var harvested = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var result in confirmedResults)
        {
            if (Array.IndexOf(HarvestableKinds, result.Kind) < 0)
            {
                continue;
            }

            var segments = result.MatchedSymbol.Split('.');
            var maxSuffixLength = Math.Min(segments.Length - 1, MaxHarvestedSuffixesPerIdentifier);
            if (maxSuffixLength < 1)
            {
                // Single-segment MatchedSymbol -- nothing shorter than the full string to harvest.
                continue;
            }

            if (!harvested.TryGetValue(result.TargetName, out var suffixSet))
            {
                suffixSet = new HashSet<string>(StringComparer.Ordinal);
                harvested[result.TargetName] = suffixSet;
            }

            for (var length = maxSuffixLength; length >= 1; length--)
            {
                suffixSet.Add(string.Join('.', segments.Skip(segments.Length - length)));
            }
        }

        return harvested;
    }
}
