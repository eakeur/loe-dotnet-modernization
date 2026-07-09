using System.Text;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Reporting;

/// <summary>
/// Exports a detailed usage-count report in Markdown for a set of <see cref="UsageResult"/>
/// entries. Unlike <see cref="MarkdownJsonReportExporter"/> (which only summarizes usage counts
/// alongside the rest of the solution report), this exporter lists every individual file:line
/// occurrence, grouped by matched symbol and by project — intended for drilling into a single
/// project or a single package's usages (the caller pre-filters <paramref name="usageResults"/>
/// to the subset of interest, e.g. all results whose <c>ProjectPath</c> matches one project).
///
/// <para>
/// <b>Output path convention:</b> always writes Markdown, regardless of the extension of
/// <paramref name="outputPath"/> — there is no companion binary/JSON format for this report.
/// </para>
/// </summary>
public sealed class UsageReportExporter
{
    public async Task ExportAsync(
        IReadOnlyList<UsageResult> usageResults,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var content = RenderMarkdown(usageResults);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, content, cancellationToken);
    }

    private static string RenderMarkdown(IReadOnlyList<UsageResult> usageResults)
    {
        var sb = new StringBuilder();

        var confirmed = usageResults.Where(u => u.Confidence == UsageConfidence.Confirmed).ToList();
        var textMatch = usageResults.Where(u => u.Confidence == UsageConfidence.TextMatch).ToList();
        var suffixMatch = usageResults.Where(u => u.Confidence == UsageConfidence.SuffixMatch).ToList();

        sb.AppendLine("# Usage Report");
        sb.AppendLine();
        sb.AppendLine($"**Total occurrences:** {usageResults.Count}");
        sb.AppendLine($"**Confirmed (Roslyn syntax match):** {confirmed.Count}");
        sb.AppendLine($"**Possible (text match only):** {textMatch.Count}");
        sb.AppendLine($"**Speculative (harvested partial match):** {suffixMatch.Count}");
        sb.AppendLine();

        sb.AppendLine("## Confirmed usages");
        sb.AppendLine();
        sb.AppendLine(
            "Matched by the Roslyn syntax-tree pass (using directives, fully-qualified type " +
            "references, member accesses, attributes, base types/interfaces). High confidence.");
        sb.AppendLine();
        sb.AppendLine($"**Count:** {confirmed.Count}");
        sb.AppendLine();
        AppendOccurrenceTable(sb, confirmed);

        sb.AppendLine("## Possible usages (text match only - verify manually)");
        sb.AppendLine();
        sb.AppendLine(
            "Matched only by the plain word-boundary text-search pass - not confirmed by Roslyn's " +
            "syntax analysis. May include comments, string literals, or coincidental name " +
            "collisions; treat these as leads for manual review, not ground truth.");
        sb.AppendLine();
        sb.AppendLine($"**Count:** {textMatch.Count}");
        sb.AppendLine();
        AppendOccurrenceTable(sb, textMatch);

        sb.AppendLine("## Speculative usages (harvested partial match - high false-positive risk, verify manually)");
        sb.AppendLine();
        sb.AppendLine(
            "Matched by searching for a progressively-shortened suffix harvested from a *different*, " +
            "already-confirmed qualified reference to the same target elsewhere in the solution (e.g. " +
            "a confirmed \"System.Web.HttpContext\" reference seeds a search for the bare \"HttpContext\"). " +
            "Catches bare/unqualified identifier usages neither of the other two passes can see, but at " +
            "the weakest confidence of the three tiers: short/common harvested terms (a type named " +
            "\"Client\" or \"Manager\") are a real source of noise. Treat every row here as a lead to " +
            "verify manually, not ground truth.");
        sb.AppendLine();
        sb.AppendLine($"**Count:** {suffixMatch.Count}");
        sb.AppendLine();
        AppendOccurrenceTable(sb, suffixMatch);

        sb.AppendLine("## Counts by Matched Symbol");
        sb.AppendLine();
        sb.AppendLine("| Symbol | Count |");
        sb.AppendLine("|---|---|");
        foreach (var group in usageResults
                     .GroupBy(u => u.MatchedSymbol)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| {group.Key} | {group.Count()} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Counts by Project");
        sb.AppendLine();
        sb.AppendLine("| Project | Count |");
        sb.AppendLine("|---|---|");
        foreach (var group in usageResults
                     .GroupBy(u => u.ProjectPath ?? "(unknown)")
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| {group.Key} | {group.Count()} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Occurrences by Symbol");
        sb.AppendLine();
        foreach (var symbolGroup in usageResults
                     .GroupBy(u => u.MatchedSymbol)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"### {symbolGroup.Key}");
            sb.AppendLine();
            sb.AppendLine("| Project | File | Line | Kind | Snippet |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var usage in symbolGroup.OrderBy(u => u.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.LineNumber))
            {
                var snippet = usage.CodeSnippet?.Replace("|", "\\|") ?? "";
                sb.AppendLine($"| {usage.ProjectPath ?? "(unknown)"} | {usage.FilePath} | {usage.LineNumber} | {usage.Kind} | `{snippet}` |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Renders a flat file/line occurrence table for a single confidence bucket (used by
    /// the "Confirmed usages" / "Possible usages" sections). Emits a one-line placeholder instead
    /// of an empty table when <paramref name="usages"/> is empty.</summary>
    private static void AppendOccurrenceTable(StringBuilder sb, IReadOnlyList<UsageResult> usages)
    {
        if (usages.Count == 0)
        {
            sb.AppendLine("_None._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Project | File | Line | Kind | Symbol | Snippet |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var usage in usages
                     .OrderBy(u => u.FilePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(u => u.LineNumber))
        {
            var snippet = usage.CodeSnippet?.Replace("|", "\\|") ?? "";
            sb.AppendLine(
                $"| {usage.ProjectPath ?? "(unknown)"} | {usage.FilePath} | {usage.LineNumber} | " +
                $"{usage.Kind} | {usage.MatchedSymbol} | `{snippet}` |");
        }
        sb.AppendLine();
    }
}
