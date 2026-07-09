using DotNetModAssess.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Detects <c>AppDomain</c> usage - <c>AppDomain.CreateDomain(...)</c>, <c>AppDomain.CurrentDomain</c>,
/// <c>AppDomain.Unload(...)</c>, and classes deriving from <c>MarshalByRefObject</c> (the base
/// type cross-AppDomain marshaling relies on).
///
/// <para>This is arguably the hardest blocker of the five this module detects: AppDomains do not
/// exist at all in .NET Core/5+ (see <see cref="Description"/>) - there is no compatibility shim,
/// no polyfill, no "mostly works" story. Any confirmed usage here means the isolation/hot-reload/
/// plugin-unloading design it supports must be redesigned from scratch (e.g. around
/// <c>AssemblyLoadContext</c> and/or separate processes), not merely ported.</para>
///
/// <para>CONFIDENCE RATIONALE: every finding here is reported at
/// <see cref="UsageConfidence.Confirmed"/>, with no weaker tier - unlike WCF/WPF/ConfigurationManager,
/// there is no meaningfully distinct "loose namespace mention" signal to grade down to:
/// <c>AppDomain</c> and <c>MarshalByRefObject</c> live directly in the <c>System</c> namespace, so
/// there's no narrow corroborating "using" directive to check the way <c>System.ServiceModel</c>/
/// <c>System.Windows</c>/<c>System.Configuration</c> give the other detectors (nearly every C# file
/// already has <c>System</c> in scope). Both identifiers are also rare enough in real code that the
/// bare-identifier collision risk this module accepts elsewhere is negligible here.</para>
/// </summary>
public sealed class AppDomainPatternDetector(ILogger<AppDomainPatternDetector>? logger = null) : ILegacyPatternDetector
{
    public string PatternName => "AppDomain";

    public string Description =>
        "AppDomains do not exist at all in .NET Core/5+ - there is no compatibility shim. Any " +
        "AppDomain.CreateDomain/Unload/CurrentDomain usage, or MarshalByRefObject-based cross-domain " +
        "marshaling, requires a genuine redesign (AssemblyLoadContext, separate processes, ...), not " +
        "a simple port.";

    private static readonly HashSet<string> ConfirmedMemberNames = new(StringComparer.Ordinal)
    {
        "CreateDomain", "CurrentDomain", "Unload",
    };

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

            var matched = MatchAppDomainMember(segments);
            if (matched is null)
            {
                continue;
            }

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, memberAccess);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, matched, UsageReferenceKind.MemberAccess, UsageConfidence.Confirmed, "AppDomain"));
        }

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDecl.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in classDecl.BaseList.Types)
            {
                if (LegacyPatternSourceHelper.GetSimpleTypeName(baseType.Type) != "MarshalByRefObject")
                {
                    continue;
                }

                var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, baseType);
                results.Add(LegacyPatternSourceHelper.BuildResult(
                    filePath, project, line, snippet, baseType.Type.ToString(), UsageReferenceKind.BaseTypeOrInterface, UsageConfidence.Confirmed, "AppDomain"));
            }
        }

        return results;
    }

    /// <summary>Finds "AppDomain" (bare or fully qualified as "System.AppDomain") immediately
    /// followed by one of <see cref="ConfirmedMemberNames"/> anywhere in the flattened chain, and
    /// returns the two-segment "AppDomain.Member" text to report - e.g. both
    /// "AppDomain.CurrentDomain" and "System.AppDomain.CurrentDomain.BaseDirectory" (a chain with
    /// a trailing member) resolve to "AppDomain.CurrentDomain".</summary>
    private static string? MatchAppDomainMember(IReadOnlyList<string> segments)
    {
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (segments[i] == "AppDomain" && ConfirmedMemberNames.Contains(segments[i + 1]))
            {
                return string.Join('.', segments.Skip(i).Take(2));
            }
        }

        return null;
    }
}
