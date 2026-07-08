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

        sb.AppendLine("# Usage Report");
        sb.AppendLine();
        sb.AppendLine($"**Total occurrences:** {usageResults.Count}");
        sb.AppendLine();

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
}
