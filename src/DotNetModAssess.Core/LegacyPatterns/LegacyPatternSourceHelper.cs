using DotNetModAssess.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Shared, low-level plumbing reused by every <see cref="ILegacyPatternDetector"/> in this
/// namespace: walking a project's own directory for files (the same "walk the .csproj's
/// directory, skip bin/obj" convention <c>RoslynUsageScanner</c> uses under
/// <c>UsageScanning/</c>), and small syntax-tree helpers (flattening a dotted name/member-access
/// chain, resolving a type's simple name, computing a result's line/snippet). This is
/// deliberately an independent copy rather than a shared reference into <c>UsageScanning/</c> -
/// this module owns its own files and must not modify that one - but the *pattern-matching
/// logic* that actually flags each legacy idiom lives in each detector class, not here; this
/// file only holds the generic wiring every detector would otherwise duplicate.
/// </summary>
internal static class LegacyPatternSourceHelper
{
    internal static IReadOnlyList<string> EnumerateSourceFiles(ProjectModel project) =>
        EnumerateFiles(project, "*.cs");

    internal static IReadOnlyList<string> EnumerateFiles(ProjectModel project, string searchPattern)
    {
        var projectDir = TryGetProjectDirectory(project.Path);
        if (projectDir is null || !Directory.Exists(projectDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(projectDir, searchPattern, SearchOption.AllDirectories)
            .Where(f => !IsUnderExcludedDirectory(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    internal static string? TryGetProjectDirectory(string projectPath)
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

    internal static bool IsUnderExcludedDirectory(string filePath)
    {
        var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s =>
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Line number (1-based) and trimmed source line text for a given node, used as the
    /// <see cref="UsageResult.LineNumber"/>/<see cref="UsageResult.CodeSnippet"/> for every
    /// source-level (non project-file-level) finding.</summary>
    internal static (int LineNumber, string? Snippet) GetLineInfo(SourceText sourceText, SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        var lineIndex = lineSpan.StartLinePosition.Line;
        var lineNumber = lineIndex + 1;

        string? snippet = lineIndex >= 0 && lineIndex < sourceText.Lines.Count
            ? sourceText.Lines[lineIndex].ToString().Trim()
            : null;

        return (lineNumber, snippet);
    }

    internal static UsageResult BuildResult(
        string filePath,
        ProjectModel project,
        int lineNumber,
        string? snippet,
        string matchedSymbol,
        UsageReferenceKind kind,
        UsageConfidence confidence,
        string patternName) => new()
        {
            FilePath = filePath,
            LineNumber = lineNumber,
            MatchedSymbol = matchedSymbol,
            Kind = kind,
            ProjectPath = project.Path,
            CodeSnippet = snippet,
            Confidence = confidence,
            TargetName = patternName,
        };

    /// <summary>True for a <c>using</c> directive this module's detectors can meaningfully
    /// compare by dotted name - i.e. not aliased and not <c>using static</c> (same deferral
    /// RoslynUsageScanner makes, for the same reason: their Name shape/semantics differ from a
    /// plain namespace import).</summary>
    internal static bool IsPlainNamespaceUsing(UsingDirectiveSyntax usingDirective) =>
        usingDirective.Name is not null
        && usingDirective.Alias is null
        && usingDirective.StaticKeyword.IsKind(SyntaxKind.None);

    /// <summary>True if a dotted using-directive name equals or is nested under <paramref name="namespaceName"/>
    /// (e.g. "System.Windows.Controls" is nested under "System.Windows").</summary>
    internal static bool NameEqualsOrNestedUnder(string dottedName, string namespaceName) =>
        dottedName == namespaceName || dottedName.StartsWith(namespaceName + ".", StringComparison.Ordinal);

    /// <summary>Rightmost identifier text of a type reference, unwrapping qualification/nullability
    /// (e.g. "System.ServiceModel.ChannelFactory&lt;T&gt;" and "ChannelFactory&lt;T&gt;" both give
    /// "ChannelFactory"). Returns null for shapes this module doesn't special-case (arrays, tuples,
    /// predefined types, ...).</summary>
    internal static string? GetSimpleTypeName(TypeSyntax? type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        QualifiedNameSyntax qualified => GetSimpleTypeName(qualified.Right),
        NullableTypeSyntax nullable => GetSimpleTypeName(nullable.ElementType),
        _ => null,
    };

    /// <summary>Simple attribute name with any "Attribute" suffix stripped (e.g. an attribute
    /// written as <c>[ServiceContract]</c> or <c>[ServiceContractAttribute]</c> or
    /// <c>[System.ServiceModel.ServiceContract]</c> all resolve to "ServiceContract"), so
    /// detectors can match on the C# idiom regardless of how verbosely the author wrote it.</summary>
    internal static string GetAttributeSimpleName(NameSyntax name)
    {
        var text = name switch
        {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => name.ToString(),
        };

        return text.EndsWith("Attribute", StringComparison.Ordinal)
            ? text[..^"Attribute".Length]
            : text;
    }

    /// <summary>True if a member-access node is the outermost link of its chain (its parent isn't
    /// another member access with this node as the left-hand expression) - i.e. flattening from
    /// here captures the whole dotted chain rather than a sub-slice of it.</summary>
    internal static bool IsTopOfMemberAccessChain(MemberAccessExpressionSyntax node) =>
        node.Parent is not MemberAccessExpressionSyntax parentAccess || parentAccess.Expression != node;

    /// <summary>Flattens a simple identifier/generic-name/member-access chain into its dotted
    /// segments (e.g. "System.Configuration.ConfigurationManager.AppSettings" becomes
    /// ["System","Configuration","ConfigurationManager","AppSettings"]). Returns null for any
    /// expression shape other than a plain dotted chain of identifiers (invocations, casts,
    /// conditional access, ...) - same v1 scope RoslynUsageScanner's own flattening uses.</summary>
    internal static IReadOnlyList<string>? FlattenExpression(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => [id.Identifier.Text],
        GenericNameSyntax generic => [generic.Identifier.Text],
        MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access =>
            Combine(FlattenExpression(access.Expression), FlattenExpression(access.Name)),
        _ => null,
    };

    private static IReadOnlyList<string>? Combine(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
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
}
