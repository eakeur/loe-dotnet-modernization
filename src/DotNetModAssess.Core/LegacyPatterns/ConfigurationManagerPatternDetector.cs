using DotNetModAssess.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Detects <c>System.Configuration.ConfigurationManager</c> member-access idioms - reading
/// <c>AppSettings</c>/<c>ConnectionStrings</c>/<c>GetSection(...)</c> off it - which is the
/// classic ambient, static, XML-config-file-shaped API .NET Framework apps use to read
/// app.config/web.config.
///
/// <para>CONFIDENCE RATIONALE - this pattern has its own "bare identifier" problem, the same one
/// <c>RoslynUsageScanner</c>'s own class docs describe for its v1 scope: a syntax-only scan cannot
/// tell whether a bare <c>ConfigurationManager.AppSettings[...]</c> actually binds to
/// <c>System.Configuration.ConfigurationManager</c> without a semantic model:</para>
/// <list type="bullet">
/// <item><see cref="UsageConfidence.Confirmed"/> - either the member-access chain is fully
/// qualified (<c>System.Configuration.ConfigurationManager.AppSettings</c>, no ambiguity at all),
/// or it's a bare <c>ConfigurationManager.*</c> access <em>and</em> the same file has a plain
/// <c>using System.Configuration;</c> directive corroborating that the bare name plausibly binds
/// there.</item>
/// <item><see cref="UsageConfidence.TextMatch"/> - a bare <c>ConfigurationManager.*</c> access
/// with no corroborating <c>using System.Configuration;</c> in the same file. Per the module's
/// own spec this is deliberately still reported (not skipped) - losing recall on a specific,
/// migration-relevant idiom seemed worse than the false-positive risk of an unrelated
/// project-defined "ConfigurationManager" class, and callers already know to treat
/// <see cref="UsageConfidence.TextMatch"/> as "worth a manual look" from the generic usage
/// scanner's own UI convention.</item>
/// </list>
/// <para>A bare <c>using System.Configuration;</c> with no <c>ConfigurationManager</c> access
/// anywhere in the file is deliberately NOT reported on its own - that namespace also contains
/// many types unrelated to this specific migration blocker (e.g. <c>ConfigurationSection</c>
/// authoring types), so a bare import isn't itself evidence of the idiom this detector exists to
/// flag.</para>
/// </summary>
public sealed class ConfigurationManagerPatternDetector(ILogger<ConfigurationManagerPatternDetector>? logger = null) : ILegacyPatternDetector
{
    public string PatternName => "ConfigurationManager";

    public string Description =>
        "System.Configuration.ConfigurationManager reads app.config/web.config XML via a static, " +
        "ambient API with no built-in equivalent on modern .NET - migrations typically replace it " +
        "with the IConfiguration/appsettings.json model, which changes every call site.";

    public async Task<IReadOnlyList<UsageResult>> DetectAsync(SolutionModel solution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        logger?.LogDebug("Running {PatternName} detector across {ProjectCount} projects", PatternName, solution.Projects.Count);
        var results = new List<UsageResult>();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in LegacyPatternSourceHelper.EnumerateSourceFiles(project))
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.AddRange(await ScanFileAsync(file, project, cancellationToken).ConfigureAwait(false));
            }
        }

        logger?.LogInformation("{PatternName} detector found {FindingCount} findings", PatternName, results.Count);
        return results;
    }

    private static async Task<List<UsageResult>> ScanFileAsync(string filePath, ProjectModel project, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var sourceText = tree.GetText(cancellationToken);

        var hasConfigurationUsing = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(LegacyPatternSourceHelper.IsPlainNamespaceUsing)
            .Any(u => u.Name!.ToString() == "System.Configuration");

        var results = new List<UsageResult>();

        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!LegacyPatternSourceHelper.IsTopOfMemberAccessChain(memberAccess))
            {
                continue;
            }

            var segments = LegacyPatternSourceHelper.FlattenExpression(memberAccess);
            if (segments is null)
            {
                continue;
            }

            var match = MatchConfigurationManagerAccess(segments, hasConfigurationUsing);
            if (match is null)
            {
                continue;
            }

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, memberAccess);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, match.Value.Matched, UsageReferenceKind.MemberAccess, match.Value.Confidence, "ConfigurationManager"));
        }

        return results;
    }

    private static (string Matched, UsageConfidence Confidence)? MatchConfigurationManagerAccess(
        IReadOnlyList<string> segments, bool hasConfigurationUsing)
    {
        if (segments.Count >= 4
            && segments[0] == "System" && segments[1] == "Configuration" && segments[2] == "ConfigurationManager")
        {
            return (string.Join('.', segments.Take(4)), UsageConfidence.Confirmed);
        }

        if (segments.Count >= 2 && segments[0] == "ConfigurationManager")
        {
            var matched = string.Join('.', segments.Take(2));
            return (matched, hasConfigurationUsing ? UsageConfidence.Confirmed : UsageConfidence.TextMatch);
        }

        return null;
    }
}
