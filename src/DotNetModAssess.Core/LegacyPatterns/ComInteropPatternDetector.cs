using DotNetModAssess.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Detects COM interop idioms: the <c>[ComVisible(...)]</c>/<c>[Guid(...)]</c>/
/// <c>[InterfaceType(...)]</c>/<c>[DllImport(...)]</c> attributes, <c>using
/// System.Runtime.InteropServices;</c>, and (project-level, not source-level) any
/// <c>&lt;COMReference&gt;</c> item already parsed out of the .csproj into
/// <see cref="LegacyCouplingSignals.ComReferences"/>.
///
/// <para>CONFIDENCE RATIONALE (see <see cref="WcfPatternDetector"/> for the shared policy this
/// mirrors):</para>
/// <list type="bullet">
/// <item>The four attributes are reported at <see cref="UsageConfidence.Confirmed"/> - each is a
/// specific, low-collision-risk COM interop idiom (a P/Invoke declaration, an explicit COM
/// visibility/identity/interface-shape marker), not a loose namespace guess.</item>
/// <item>A bare <c>using System.Runtime.InteropServices;</c> directive with none of those
/// attributes present in the same file is reported at <see cref="UsageConfidence.TextMatch"/> -
/// the namespace also contains marshaling helper types unrelated to COM interop specifically
/// (e.g. <c>Marshal</c> static helpers used for plain P/Invoke-free struct layout), so on its own
/// it's weaker evidence than an actual matched attribute.</item>
/// <item>Each entry in <see cref="LegacyCouplingSignals.ComReferences"/> (a <c>&lt;COMReference&gt;</c>
/// item the parser already extracted from the raw .csproj XML) is reported at
/// <see cref="UsageConfidence.Confirmed"/> - there's no ambiguity about a project file literally
/// referencing a COM type library. These are project-level, not source-level: they carry
/// <see cref="UsageResult.FilePath"/> = the project's own path and <see cref="UsageResult.LineNumber"/> = 1
/// (no specific line - a COMReference is a project-file item, not a source location) and
/// <see cref="UsageReferenceKind.Other"/>, keeping them distinguishable in the UI from the
/// source-level attribute findings above.</item>
/// </list>
/// </summary>
public sealed class ComInteropPatternDetector(ILogger<ComInteropPatternDetector>? logger = null) : ILegacyPatternDetector
{
    public string PatternName => "COM Interop";

    public string Description =>
        "COM interop ([ComVisible]/[Guid]/[InterfaceType], [DllImport], <COMReference> type " +
        "libraries) depends on the Windows COM runtime and classic COM registration, neither of " +
        "which exist on Linux - COM-based integrations must be replaced with a native library, a " +
        "service call, or dropped entirely.";

    private static readonly HashSet<string> ConfirmedAttributeNames = new(StringComparer.Ordinal)
    {
        "ComVisible", "Guid", "InterfaceType", "DllImport",
    };

    public async Task<IReadOnlyList<UsageResult>> DetectAsync(SolutionModel solution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        logger?.LogDebug("Running {PatternName} detector across {ProjectCount} projects", PatternName, solution.Projects.Count);
        var results = new List<UsageResult>();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var comReference in project.Metadata.LegacySignals.ComReferences)
            {
                results.Add(new UsageResult
                {
                    FilePath = project.Path,
                    LineNumber = 1,
                    MatchedSymbol = comReference,
                    Kind = UsageReferenceKind.Other,
                    ProjectPath = project.Path,
                    CodeSnippet = null,
                    Confidence = UsageConfidence.Confirmed,
                    TargetName = "COM Interop",
                });
            }

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
        var hasConfirmedAttribute = false;

        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            if (!ConfirmedAttributeNames.Contains(LegacyPatternSourceHelper.GetAttributeSimpleName(attribute.Name)))
            {
                continue;
            }

            hasConfirmedAttribute = true;
            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, attribute);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, attribute.Name.ToString(), UsageReferenceKind.Attribute, UsageConfidence.Confirmed, "COM Interop"));
        }

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (!LegacyPatternSourceHelper.IsPlainNamespaceUsing(usingDirective))
            {
                continue;
            }

            var joined = usingDirective.Name!.ToString();
            if (!LegacyPatternSourceHelper.NameEqualsOrNestedUnder(joined, "System.Runtime.InteropServices"))
            {
                continue;
            }

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, usingDirective);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, joined, UsageReferenceKind.UsingDirective,
                hasConfirmedAttribute ? UsageConfidence.Confirmed : UsageConfidence.TextMatch, "COM Interop"));
        }

        return results;
    }
}
