using DotNetModAssess.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetModAssess.Core.UsageScanning;

/// <summary>
/// Roslyn syntax-tree-based implementation of <see cref="IUsageScanner"/>.
///
/// <para>
/// V1 SCOPE (intentionally a plain syntax scan, no semantic model / compilation):
/// </para>
/// <list type="bullet">
/// <item>For each project in the solution, the project's ".csproj" directory (excluding any
/// "bin"/"obj" subdirectories) is walked for "*.cs" files. Each file is parsed independently
/// with <see cref="CSharpSyntaxTree.ParseText(string, CSharpParseOptions?, string, System.Text.Encoding?, System.Threading.CancellationToken)"/>.</item>
/// <item>"Targets" (the things whose usage we're trying to measure) are built once for the
/// whole solution: every project's approximate root namespace, plus every package id
/// referenced anywhere in the solution.</item>
/// <item>Matches are reported for: <c>using</c> directives naming a target namespace (or a
/// nested namespace under it), fully-qualified type references (declarations, casts,
/// generics, attributes, base lists, etc.), and member-access expressions rooted at a
/// fully-qualified target type (e.g. "System.Web.HttpContext.Current").</item>
/// </list>
///
/// <para>
/// EXPLICITLY DEFERRED TO A LATER SEMANTIC-MODEL PASS (per the module's own spec, which allows
/// this for v1):
/// </para>
/// <list type="bullet">
/// <item>Bare/unqualified identifier resolution. E.g. given <c>using System.Web;</c> followed
/// later by <c>HttpContext.Current</c> (no further qualification), this scanner cannot tell
/// whether "HttpContext" binds to that namespace or to some unrelated same-named symbol,
/// because that requires building a <c>Compilation</c> and resolving symbols. Only fully
/// qualified references (e.g. "System.Web.HttpContext.Current") are detected as type
/// references / member accesses. This is the single biggest source of under-counting in v1.</item>
/// <item><c>using static</c> directives and <c>using</c> aliases are skipped entirely (their
/// Name shape/semantics differ from a plain namespace import and would need special-casing).</item>
/// <item>Project "namespaces" are approximated from <see cref="ProjectMetadata.RootNamespace"/>
/// (falling back to <see cref="ProjectMetadata.AssemblyName"/>, then <see cref="ProjectModel.Name"/>)
/// since there is no compiled namespace inventory per project at this stage. A project that
/// exposes types under multiple, unrelated namespaces will be under-matched.</item>
/// <item>Package "namespaces" are approximated as the literal NuGet package id (e.g.
/// "Newtonsoft.Json"). This works when a package's primary namespace equals its package id,
/// but under-matches packages where they differ (e.g. package "Serilog.AspNetCore" exposes
/// namespace "Serilog", not "Serilog.AspNetCore"). Building a real NuGet-content-aware
/// namespace resolver is out of scope for v1 per the module spec.</item>
/// </list>
///
/// <para>
/// SECOND PASS: to improve recall on the limitations above without building a semantic-model
/// resolver, <see cref="ScanAsync"/> unions this Roslyn syntax pass with an independent
/// <see cref="TextSearchUsageScanner"/> pass - a plain word-boundary text search for the same
/// <see cref="UsageTarget"/> names across a broader file set (not just ".cs"). Roslyn-syntax
/// matches are tagged <see cref="UsageConfidence.Confirmed"/>; anything only the text pass found
/// is tagged <see cref="UsageConfidence.TextMatch"/> and de-duplicated against this pass's results
/// by (FilePath, LineNumber, TargetName) so the same physical occurrence is never reported
/// twice under two different confidence tiers. See <see cref="TextSearchUsageScanner"/> for that
/// pass's own scope and limitations.
/// </para>
///
/// <para>
/// THIRD PASS: <see cref="SuffixHarvestUsageScanner"/> harvests a per-target vocabulary of
/// progressively-shortened suffixes from this pass's own <see cref="UsageConfidence.Confirmed"/>
/// results (e.g. a confirmed "System.Web.HttpContext" seeds searches for "Web.HttpContext" and bare
/// "HttpContext") and searches for those too, catching the classic bare-identifier case neither of
/// the other two passes can. Tagged <see cref="UsageConfidence.SuffixMatch"/> - the weakest tier -
/// and likewise de-duplicated by (FilePath, LineNumber, TargetName) against both prior passes'
/// results. See <see cref="SuffixHarvestUsageScanner"/> for that pass's own scope.
/// </para>
///
/// <para>
/// Every result from all three passes carries <see cref="UsageResult.TargetName"/> set to the
/// owning <see cref="UsageTarget.Name"/>, regardless of what the pass's own <c>MatchedSymbol</c>
/// text actually found - this is what makes the (FilePath, LineNumber, TargetName) dedup key (and
/// any "usages of this target" lookup) meaningful across passes that otherwise report different
/// literal matched text for the same underlying target.
/// </para>
/// </summary>
public sealed class RoslynUsageScanner : IUsageScanner
{
    public async Task<IReadOnlyList<UsageResult>> ScanAsync(SolutionModel solution, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var targets = BuildTargets(solution);
        var confirmedResults = new List<UsageResult>();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectTargets = GetTargetsForProject(targets, project);

            if (projectTargets.Count == 0)
            {
                continue;
            }

            progress?.Report($"Scanning {project.Name} for usages...");
            foreach (var file in EnumerateSourceFiles(project))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileResults = await ScanFileAsync(file, project, projectTargets, cancellationToken).ConfigureAwait(false);
                confirmedResults.AddRange(fileResults);
            }
        }

        var orderedConfirmed = confirmedResults
            .OrderBy(r => r.FilePath, StringComparer.Ordinal)
            .ThenBy(r => r.LineNumber)
            .ThenBy(r => r.MatchedSymbol, StringComparer.Ordinal)
            .ToList();

        progress?.Report("Scanning source for text-match usages...");
        var textMatchResults = await TextSearchUsageScanner.ScanAsync(solution, targets, orderedConfirmed, cancellationToken)
            .ConfigureAwait(false);

        var reportedSoFar = new List<UsageResult>(orderedConfirmed.Count + textMatchResults.Count);
        reportedSoFar.AddRange(orderedConfirmed);
        reportedSoFar.AddRange(textMatchResults);

        progress?.Report("Harvesting partial-identifier matches...");
        var suffixMatchResults = await SuffixHarvestUsageScanner.ScanAsync(solution, targets, orderedConfirmed, reportedSoFar, cancellationToken)
            .ConfigureAwait(false);

        // Ordering convention: every Confirmed (Roslyn) result first, in that pass's own sort
        // order, followed by every TextMatch result, then every SuffixMatch result, each in its
        // own sort order. This keeps the highest-confidence results first without interleaving
        // the three passes.
        var combined = new List<UsageResult>(orderedConfirmed.Count + textMatchResults.Count + suffixMatchResults.Count);
        combined.AddRange(orderedConfirmed);
        combined.AddRange(textMatchResults);
        combined.AddRange(suffixMatchResults);
        return combined;
    }

    /// <summary>A single thing whose usage we're looking for: a project's approximate root
    /// namespace, or a package id. <paramref name="OwningProjectPath"/> is non-null only for
    /// project targets, and is used to avoid a project reporting "usage" of its own namespace.
    /// Internal (rather than private) so <see cref="TextSearchUsageScanner"/> can search for the
    /// exact same set of targets as this pass, per the module's design.</summary>
    internal sealed record UsageTarget(string Name, string? OwningProjectPath);

    /// <summary>The subset of <paramref name="targets"/> that a given <paramref name="project"/>
    /// should be scanned against - i.e. every target except the project's own namespace (a
    /// project doesn't "use" itself). Shared by both the Roslyn pass and
    /// <see cref="TextSearchUsageScanner"/> so they agree on what counts as a self-reference.</summary>
    internal static IReadOnlyList<UsageTarget> GetTargetsForProject(IReadOnlyList<UsageTarget> targets, ProjectModel project) =>
        targets
            .Where(t => t.OwningProjectPath is null || !PathsEqual(t.OwningProjectPath, project.Path))
            .ToList();

    internal static IReadOnlyList<UsageTarget> BuildTargets(SolutionModel solution)
    {
        var targets = new List<UsageTarget>();

        foreach (var project in solution.Projects)
        {
            var ns = project.Metadata.RootNamespace;
            if (string.IsNullOrWhiteSpace(ns))
            {
                ns = project.Metadata.AssemblyName;
            }
            if (string.IsNullOrWhiteSpace(ns))
            {
                ns = project.Name;
            }

            if (!string.IsNullOrWhiteSpace(ns))
            {
                targets.Add(new UsageTarget(ns, project.Path));
            }
        }

        var packageIds = solution.Projects
            .SelectMany(p => p.PackageReferences)
            .Select(p => p.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal);

        targets.AddRange(packageIds.Select(id => new UsageTarget(id, OwningProjectPath: null)));

        return targets
            .DistinctBy(t => (t.Name, t.OwningProjectPath))
            .ToList();
    }

    private static IReadOnlyList<string> EnumerateSourceFiles(ProjectModel project)
    {
        var projectDir = TryGetProjectDirectory(project.Path);
        if (projectDir is null || !Directory.Exists(projectDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsUnderExcludedDirectory(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Walks a project's directory (same "bin"/"obj" exclusion convention as
    /// <see cref="EnumerateSourceFiles"/>) for files matching any of <paramref name="extensions"/>.
    /// Used by <see cref="TextSearchUsageScanner"/> to search a broader file set than the Roslyn
    /// pass's ".cs"-only scan (".razor", ".cshtml", ".config", ".json", ".xml", ...).</summary>
    internal static IReadOnlyList<string> EnumerateFilesForTextSearch(ProjectModel project, IReadOnlyCollection<string> extensions)
    {
        var projectDir = TryGetProjectDirectory(project.Path);
        if (projectDir is null || !Directory.Exists(projectDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(projectDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsUnderExcludedDirectory(f) && extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static string? TryGetProjectDirectory(string projectPath)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(projectPath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsUnderExcludedDirectory(string filePath)
    {
        var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s =>
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<List<UsageResult>> ScanFileAsync(
        string filePath,
        ProjectModel project,
        IReadOnlyList<UsageTarget> targets,
        CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var sourceText = tree.GetText(cancellationToken);

        var results = new List<UsageResult>();

        ScanUsingDirectives(root, filePath, project, sourceText, targets, results);
        ScanTypePositionNames(root, filePath, project, sourceText, targets, results);
        ScanMemberAccessChains(root, filePath, project, sourceText, targets, results);

        return results;
    }

    private static void ScanUsingDirectives(
        SyntaxNode root,
        string filePath,
        ProjectModel project,
        SourceText sourceText,
        IReadOnlyList<UsageTarget> targets,
        List<UsageResult> results)
    {
        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDirective.Alias is not null || !usingDirective.StaticKeyword.IsKind(SyntaxKind.None))
            {
                // Aliased ("using X = Y.Z;") and "using static Y.Z;" directives are deferred (see class docs).
                continue;
            }

            var name = usingDirective.Name;
            if (name is null)
            {
                continue;
            }

            var segments = FlattenName(name);
            if (segments is null)
            {
                continue;
            }

            var joined = string.Join('.', segments);

            foreach (var target in targets)
            {
                if (joined == target.Name || joined.StartsWith(target.Name + ".", StringComparison.Ordinal))
                {
                    AddResult(results, filePath, project, sourceText, name, joined, UsageReferenceKind.UsingDirective, target.Name);
                }
            }
        }
    }

    private static void ScanTypePositionNames(
        SyntaxNode root,
        string filePath,
        ProjectModel project,
        SourceText sourceText,
        IReadOnlyList<UsageTarget> targets,
        List<UsageResult> results)
    {
        foreach (var nameNode in root.DescendantNodes().OfType<NameSyntax>())
        {
            if (!IsTopOfNameChain(nameNode))
            {
                continue;
            }

            var segments = FlattenName(nameNode);
            if (segments is null)
            {
                continue;
            }

            foreach (var target in targets)
            {
                var matchedPrefix = MatchTargetPrefix(segments, target.Name);
                if (matchedPrefix is null)
                {
                    continue;
                }

                var kind = ClassifyNamePosition(nameNode);
                AddResult(results, filePath, project, sourceText, nameNode, matchedPrefix, kind, target.Name);
            }
        }
    }

    private static void ScanMemberAccessChains(
        SyntaxNode root,
        string filePath,
        ProjectModel project,
        SourceText sourceText,
        IReadOnlyList<UsageTarget> targets,
        List<UsageResult> results)
    {
        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!IsTopOfChain(memberAccess))
            {
                continue;
            }

            var segments = FlattenExpression(memberAccess);
            if (segments is null)
            {
                continue;
            }

            foreach (var target in targets)
            {
                var matchedPrefix = MatchTargetPrefix(segments, target.Name);
                if (matchedPrefix is null)
                {
                    continue;
                }

                AddResult(results, filePath, project, sourceText, memberAccess, matchedPrefix, UsageReferenceKind.MemberAccess, target.Name);
            }
        }
    }

    /// <summary>
    /// If <paramref name="chainSegments"/> starts with all of <paramref name="target"/>'s dotted
    /// segments, returns the dotted string covering the target plus (at most) one more segment
    /// -- i.e. "the type" being referenced under that namespace/package. Returns null if the
    /// chain does not fully contain the target as a leading prefix.
    /// </summary>
    private static string? MatchTargetPrefix(IReadOnlyList<string> chainSegments, string target)
    {
        var targetSegments = target.Split('.');
        if (chainSegments.Count < targetSegments.Length)
        {
            return null;
        }

        for (var i = 0; i < targetSegments.Length; i++)
        {
            if (!string.Equals(chainSegments[i], targetSegments[i], StringComparison.Ordinal))
            {
                return null;
            }
        }

        var take = Math.Min(targetSegments.Length + 1, chainSegments.Count);
        return string.Join('.', chainSegments.Take(take));
    }

    private static bool IsTopOfNameChain(NameSyntax node) =>
        node.Parent is not QualifiedNameSyntax
        && node.Parent is not UsingDirectiveSyntax
        && node.Parent is not BaseNamespaceDeclarationSyntax
        && node.Parent is not AliasQualifiedNameSyntax;

    private static bool IsTopOfChain(MemberAccessExpressionSyntax node) =>
        node.Parent is not MemberAccessExpressionSyntax parentAccess || parentAccess.Expression != node;

    private static IReadOnlyList<string>? FlattenName(NameSyntax name) => name switch
    {
        IdentifierNameSyntax id => [id.Identifier.Text],
        GenericNameSyntax generic => [generic.Identifier.Text],
        QualifiedNameSyntax qualified => CombineOrNull(FlattenName(qualified.Left), FlattenName(qualified.Right)),
        _ => null, // AliasQualifiedNameSyntax etc. -- unsupported in v1
    };

    private static IReadOnlyList<string>? FlattenExpression(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => [id.Identifier.Text],
        GenericNameSyntax generic => [generic.Identifier.Text],
        MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access =>
            CombineOrNull(FlattenExpression(access.Expression), FlattenExpression(access.Name)),
        _ => null,
    };

    private static IReadOnlyList<string>? CombineOrNull(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        var combined = new List<string>(left.Count + right.Count);
        combined.AddRange(left);
        combined.AddRange(right);
        return combined;
    }

    private static UsageReferenceKind ClassifyNamePosition(NameSyntax node)
    {
        var parent = node.Parent;

        if (parent is AttributeSyntax)
        {
            return UsageReferenceKind.Attribute;
        }

        if (parent is SimpleBaseTypeSyntax)
        {
            return UsageReferenceKind.BaseTypeOrInterface;
        }

        if (IsRecognizedTypeContext(parent, node))
        {
            return UsageReferenceKind.TypeReference;
        }

        return UsageReferenceKind.Other;
    }

    private static bool IsRecognizedTypeContext(SyntaxNode? parent, NameSyntax node) => parent switch
    {
        VariableDeclarationSyntax vd => vd.Type == node,
        ObjectCreationExpressionSyntax oc => oc.Type == node,
        CastExpressionSyntax ce => ce.Type == node,
        ParameterSyntax ps => ps.Type == node,
        MethodDeclarationSyntax md => md.ReturnType == node,
        PropertyDeclarationSyntax pd => pd.Type == node,
        IndexerDeclarationSyntax id => id.Type == node,
        DelegateDeclarationSyntax dd => dd.ReturnType == node,
        EventDeclarationSyntax ed => ed.Type == node,
        CatchDeclarationSyntax cd => cd.Type == node,
        ArrayTypeSyntax at => at.ElementType == node,
        DefaultExpressionSyntax de => de.Type == node,
        TypeOfExpressionSyntax tf => tf.Type == node,
        DeclarationPatternSyntax dp => dp.Type == node,
        TypeArgumentListSyntax => true,
        TypeConstraintSyntax tc => tc.Type == node,
        _ => false,
    };

    private static void AddResult(
        List<UsageResult> results,
        string filePath,
        ProjectModel project,
        SourceText sourceText,
        SyntaxNode node,
        string matchedSymbol,
        UsageReferenceKind kind,
        string targetName)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        var lineIndex = lineSpan.StartLinePosition.Line;
        var lineNumber = lineIndex + 1;

        string? snippet = lineIndex >= 0 && lineIndex < sourceText.Lines.Count
            ? sourceText.Lines[lineIndex].ToString().Trim()
            : null;

        results.Add(new UsageResult
        {
            FilePath = filePath,
            LineNumber = lineNumber,
            MatchedSymbol = matchedSymbol,
            Kind = kind,
            ProjectPath = project.Path,
            CodeSnippet = snippet,
            TargetName = targetName,
        });
    }
}
