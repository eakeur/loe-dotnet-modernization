using System.Text;
using DotNetModAssess.Core.Graph;

namespace DotNetModAssess.Core.Reporting;

/// <summary>
/// Exports a <see cref="DependencyGraph"/> as either Graphviz DOT or Mermaid flowchart source.
///
/// <para>
/// <b>Output path convention:</b> the format is selected by the extension of <paramref
/// name="outputPath"/> (case-insensitive): <c>.mmd</c> or <c>.mermaid</c> writes a Mermaid
/// <c>graph TD</c> diagram; any other extension (typically <c>.dot</c> or <c>.gv</c>) writes
/// Graphviz DOT. Project nodes and package nodes are styled distinctly (box vs. ellipse), and
/// packages with a version conflict (<c>PackageModel.HasVersionConflict</c>) are flagged with a
/// distinct fill color in both formats.
/// </para>
/// </summary>
public sealed class DotMermaidGraphExporter : IGraphExporter
{
    private const string ProjectFillColor = "#dae8fc";
    private const string ProjectStrokeColor = "#6c8ebf";
    private const string PackageFillColor = "#d5e8d4";
    private const string PackageStrokeColor = "#82b366";
    private const string ConflictFillColor = "#f8cecc";
    private const string ConflictStrokeColor = "#b85450";

    public Task ExportAsync(DependencyGraph graph, string outputPath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(outputPath);

        var content = extension.ToLowerInvariant() switch
        {
            ".mmd" or ".mermaid" => RenderMermaid(graph),
            _ => RenderDot(graph)
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return File.WriteAllTextAsync(outputPath, content, cancellationToken);
    }

    private static string GetLabel(GraphNode node) => node switch
    {
        ProjectGraphNode p => p.Project.Name,
        PackageGraphNode pkg => pkg.Package.PackageId,
        _ => node.Id
    };

    private static bool IsConflictedPackage(GraphNode node) =>
        node is PackageGraphNode { Package.HasVersionConflict: true };

    private static string RenderDot(DependencyGraph graph)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph DependencyGraph {");
        sb.AppendLine("  rankdir=LR;");
        sb.AppendLine("  node [fontname=\"Helvetica\"];");
        sb.AppendLine();

        foreach (var node in graph.Nodes)
        {
            var id = DotEscape(node.Id);
            var label = DotEscape(GetLabel(node));

            if (node.Kind == GraphNodeKind.Project)
            {
                sb.AppendLine($"  \"{id}\" [label=\"{label}\", shape=box, style=filled, fillcolor=\"{ProjectFillColor}\", color=\"{ProjectStrokeColor}\"];");
            }
            else
            {
                var isConflict = IsConflictedPackage(node);
                var fill = isConflict ? ConflictFillColor : PackageFillColor;
                var stroke = isConflict ? ConflictStrokeColor : PackageStrokeColor;
                var penWidth = isConflict ? ", penwidth=2" : "";
                sb.AppendLine($"  \"{id}\" [label=\"{label}\", shape=ellipse, style=filled, fillcolor=\"{fill}\", color=\"{stroke}\"{penWidth}];");
            }
        }

        sb.AppendLine();

        foreach (var edge in graph.Edges)
        {
            var from = DotEscape(edge.FromId);
            var to = DotEscape(edge.ToId);

            if (edge.Kind == GraphEdgeKind.ProjectToProject)
            {
                sb.AppendLine($"  \"{from}\" -> \"{to}\" [label=\"project ref\", style=solid];");
            }
            else
            {
                var versionLabel = edge.ResolvedPackageVersion is null ? "" : $", label=\"{DotEscape(edge.ResolvedPackageVersion)}\"";
                sb.AppendLine($"  \"{from}\" -> \"{to}\" [style=dashed{versionLabel}];");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string RenderMermaid(DependencyGraph graph)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        // Mermaid node identifiers must be simple tokens (no slashes/dots), so map every graph
        // node id to a synthetic Nn token and keep the real id/name as the rendered label.
        var nodeIds = new Dictionary<string, string>();
        var index = 0;
        foreach (var node in graph.Nodes)
        {
            nodeIds[node.Id] = $"N{index++}";
        }

        foreach (var node in graph.Nodes)
        {
            var token = nodeIds[node.Id];
            var label = MermaidEscape(GetLabel(node));

            if (node.Kind == GraphNodeKind.Project)
            {
                sb.AppendLine($"    {token}[\"{label}\"]:::project");
            }
            else if (IsConflictedPackage(node))
            {
                sb.AppendLine($"    {token}(\"{label}\"):::packageConflict");
            }
            else
            {
                sb.AppendLine($"    {token}(\"{label}\"):::package");
            }
        }

        foreach (var edge in graph.Edges)
        {
            var from = nodeIds[edge.FromId];
            var to = nodeIds[edge.ToId];

            if (edge.Kind == GraphEdgeKind.ProjectToProject)
            {
                sb.AppendLine($"    {from} --> {to}");
            }
            else if (edge.ResolvedPackageVersion is not null)
            {
                sb.AppendLine($"    {from} -- \"{MermaidEscape(edge.ResolvedPackageVersion)}\" --> {to}");
            }
            else
            {
                sb.AppendLine($"    {from} --> {to}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"    classDef project fill:{ProjectFillColor},stroke:{ProjectStrokeColor},stroke-width:1px;");
        sb.AppendLine($"    classDef package fill:{PackageFillColor},stroke:{PackageStrokeColor},stroke-width:1px;");
        sb.AppendLine($"    classDef packageConflict fill:{ConflictFillColor},stroke:{ConflictStrokeColor},stroke-width:2px;");

        return sb.ToString();
    }

    private static string DotEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string MermaidEscape(string value) =>
        value.Replace("\"", "#quot;");
}
