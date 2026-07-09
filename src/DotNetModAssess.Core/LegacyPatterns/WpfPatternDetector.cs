using DotNetModAssess.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Detects WPF (Windows Presentation Foundation) idioms: presence of <c>.xaml</c> files, classes
/// deriving from WPF's own UI base types, <c>DependencyProperty.Register(...)</c> registration
/// calls, and <c>System.Windows*</c> namespace usage.
///
/// <para>CONFIDENCE RATIONALE (see <see cref="WcfPatternDetector"/> for the shared policy this
/// mirrors):</para>
/// <list type="bullet">
/// <item>A <c>.xaml</c> file existing anywhere in the project's own directory is reported at
/// <see cref="UsageConfidence.Confirmed"/> without parsing any C# at all - per the module's own
/// spec, this is a strong, cheap, standalone signal (SDK-style non-WPF projects essentially never
/// contain stray .xaml files) and needs no corroboration.</item>
/// <item>A class whose base list contains <c>Window</c>/<c>UserControl</c>/<c>Page</c>/
/// <c>Application</c>, and a <c>DependencyProperty.Register(...)</c> call, are both reported
/// <see cref="UsageConfidence.Confirmed"/> - both are specific WPF idioms, not merely a namespace
/// mention.</item>
/// <item>A bare <c>using System.Windows</c> (or nested namespace) directive on its own is
/// reported <see cref="UsageConfidence.TextMatch"/> - <em>unless</em> the owning project's own
/// MSBuild <c>UseWPF</c> property (<see cref="ProjectMetadata.UseWpf"/>) is already known to be
/// true, in which case it's promoted to <see cref="UsageConfidence.Confirmed"/>: at that point
/// it's not a bare namespace guess, since MSBuild itself is independently telling us this project
/// builds as a WPF project. This is the "cross-reference UseWpf as a corroborating signal" the
/// module's spec calls for, without ever emitting a finding purely from that boolean.</item>
/// </list>
/// </summary>
public sealed class WpfPatternDetector : ILegacyPatternDetector
{
    public string PatternName => "WPF";

    public string Description =>
        "WPF (System.Windows.*, XAML) is a Windows-only desktop UI framework with no runtime on " +
        ".NET on Linux at all - a WPF project can be retargeted to a modern TFM but cannot run in " +
        "a Linux container; the UI layer itself must be replaced (Blazor, MAUI, Avalonia, a web " +
        "front end, ...).";

    private static readonly HashSet<string> ConfirmedBaseTypeNames = new(StringComparer.Ordinal)
    {
        "Window", "UserControl", "Page", "Application",
    };

    public async Task<IReadOnlyList<UsageResult>> DetectAsync(SolutionModel solution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        var results = new List<UsageResult>();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var xamlFile in LegacyPatternSourceHelper.EnumerateFiles(project, "*.xaml"))
            {
                results.Add(new UsageResult
                {
                    FilePath = xamlFile,
                    LineNumber = 1,
                    MatchedSymbol = Path.GetFileName(xamlFile),
                    Kind = UsageReferenceKind.Other,
                    ProjectPath = project.Path,
                    CodeSnippet = null,
                    Confidence = UsageConfidence.Confirmed,
                    TargetName = "WPF",
                });
            }

            foreach (var file in LegacyPatternSourceHelper.EnumerateSourceFiles(project))
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.AddRange(await ScanFileAsync(file, project, cancellationToken).ConfigureAwait(false));
            }
        }

        return results;
    }

    private static async Task<List<UsageResult>> ScanFileAsync(string filePath, ProjectModel project, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var sourceText = tree.GetText(cancellationToken);

        var results = new List<UsageResult>();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDecl.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in classDecl.BaseList.Types)
            {
                var typeName = LegacyPatternSourceHelper.GetSimpleTypeName(baseType.Type);
                if (typeName is null || !ConfirmedBaseTypeNames.Contains(typeName))
                {
                    continue;
                }

                var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, baseType);
                results.Add(LegacyPatternSourceHelper.BuildResult(
                    filePath, project, line, snippet, baseType.Type.ToString(), UsageReferenceKind.BaseTypeOrInterface, UsageConfidence.Confirmed, "WPF"));
            }
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "DependencyProperty" },
                    Name.Identifier.Text: "Register",
                })
            {
                continue;
            }

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, invocation);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, "DependencyProperty.Register", UsageReferenceKind.MemberAccess, UsageConfidence.Confirmed, "WPF"));
        }

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (!LegacyPatternSourceHelper.IsPlainNamespaceUsing(usingDirective))
            {
                continue;
            }

            var joined = usingDirective.Name!.ToString();
            if (!LegacyPatternSourceHelper.NameEqualsOrNestedUnder(joined, "System.Windows"))
            {
                continue;
            }

            // Corroborated by the project's own MSBuild UseWPF property when set - see class docs.
            var confidence = project.Metadata.UseWpf ? UsageConfidence.Confirmed : UsageConfidence.TextMatch;

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, usingDirective);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, joined, UsageReferenceKind.UsingDirective, confidence, "WPF"));
        }

        return results;
    }
}
