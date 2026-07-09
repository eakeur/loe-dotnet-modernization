using DotNetModAssess.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Detects WCF (<c>System.ServiceModel</c>) service-boundary idioms - the specific shapes that
/// make a type genuinely a service contract/host/client, not just a file that happens to mention
/// the namespace somewhere.
///
/// <para>
/// CONFIDENCE RATIONALE (this reasoning is shared by every detector in this namespace, for
/// consistency - restated per-class per the module's own instructions):
/// </para>
/// <list type="bullet">
/// <item><see cref="UsageConfidence.Confirmed"/> is used for a specific, low-collision-risk
/// idiom: the <c>[ServiceContract]</c>/<c>[OperationContract]</c>/<c>[DataContract]</c>/
/// <c>[DataMember]</c> attributes (these names are essentially unambiguous - a project defining
/// its own, unrelated attribute with one of these exact names would be an active, confusing
/// choice), a <c>new ServiceHost(...)</c>/<c>new ChannelFactory&lt;T&gt;(...)</c> instantiation,
/// or a class whose base list contains <c>ClientBase&lt;T&gt;</c>. Every one of these marks an
/// actual service boundary/client - which is the entire point of this detector - so they're
/// reported at the highest confidence even though (like the rest of this v1 syntax-only module)
/// no semantic model confirms the identifier actually binds to <c>System.ServiceModel</c>.</item>
/// <item><see cref="UsageConfidence.TextMatch"/> is used only for a bare <c>using
/// System.ServiceModel</c> (or nested namespace) directive with none of the above idioms also
/// present in the same file. Importing the namespace is real corroborating evidence but doesn't
/// by itself prove a service contract/host/client idiom exists in the file (e.g. it might only
/// use some unrelated helper type from that namespace) - so it's reported one tier down, mirroring
/// how the generic usage scanner treats a bare namespace mention as weaker than a confirmed type
/// reference.</item>
/// </list>
/// </summary>
public sealed class WcfPatternDetector(ILogger<WcfPatternDetector>? logger = null) : ILegacyPatternDetector
{
    public string PatternName => "WCF";

    public string Description =>
        "System.ServiceModel service contracts/hosts/clients (SOAP/WCF) have no built-in runtime " +
        "on modern .NET - migrating usually means moving to CoreWCF, gRPC, or a REST API, all of " +
        "which change the wire contract and every client that talks to it.";

    private static readonly HashSet<string> ConfirmedAttributeNames = new(StringComparer.Ordinal)
    {
        "ServiceContract", "OperationContract", "DataContract", "DataMember",
    };

    private static readonly HashSet<string> ConfirmedInstantiatedTypeNames = new(StringComparer.Ordinal)
    {
        "ServiceHost", "ChannelFactory",
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

        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            if (!ConfirmedAttributeNames.Contains(LegacyPatternSourceHelper.GetAttributeSimpleName(attribute.Name)))
            {
                continue;
            }

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, attribute);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, attribute.Name.ToString(), UsageReferenceKind.Attribute, UsageConfidence.Confirmed, "WCF"));
        }

        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = LegacyPatternSourceHelper.GetSimpleTypeName(creation.Type);
            if (typeName is null || !ConfirmedInstantiatedTypeNames.Contains(typeName))
            {
                continue;
            }

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, creation);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, creation.Type.ToString(), UsageReferenceKind.TypeReference, UsageConfidence.Confirmed, "WCF"));
        }

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDecl.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in classDecl.BaseList.Types)
            {
                if (LegacyPatternSourceHelper.GetSimpleTypeName(baseType.Type) != "ClientBase")
                {
                    continue;
                }

                var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, baseType);
                results.Add(LegacyPatternSourceHelper.BuildResult(
                    filePath, project, line, snippet, baseType.Type.ToString(), UsageReferenceKind.BaseTypeOrInterface, UsageConfidence.Confirmed, "WCF"));
            }
        }

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (!LegacyPatternSourceHelper.IsPlainNamespaceUsing(usingDirective))
            {
                continue;
            }

            var joined = usingDirective.Name!.ToString();
            if (!LegacyPatternSourceHelper.NameEqualsOrNestedUnder(joined, "System.ServiceModel"))
            {
                continue;
            }

            var (line, snippet) = LegacyPatternSourceHelper.GetLineInfo(sourceText, usingDirective);
            results.Add(LegacyPatternSourceHelper.BuildResult(
                filePath, project, line, snippet, joined, UsageReferenceKind.UsingDirective, UsageConfidence.TextMatch, "WCF"));
        }

        return results;
    }
}
