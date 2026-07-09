using System.ComponentModel;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Mcp.Dtos;
using DotNetModAssess.Mcp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace DotNetModAssess.Mcp.Tools;

/// <summary>Tools for the eagerly-computed legacy migration-blocker pattern findings (WCF, WPF,
/// ConfigurationManager, AppDomain, COM interop).</summary>
[McpServerToolType]
public static class FindingsTools
{
    [McpServerTool(Name = "get_legacy_findings", ReadOnly = true)]
    [Description(
        "Returns known legacy migration-blocker pattern findings across the solution: WCF (e.g. " +
        "[ServiceContract]/ChannelFactory<T>), WPF, ConfigurationManager usage, AppDomain usage, and " +
        "COM interop, each as file:line evidence (not just a boolean per project - see " +
        "get_project_details' LegacySignals for the cheap per-project boolean flags this " +
        "complements). Optionally filter to a single pattern by name. Use this to identify concrete " +
        "modernization blockers and exactly where they live before proposing a migration plan.")]
    public static async Task<IReadOnlyList<UsageResultDto>> GetLegacyFindings(
        McpWorkspaceState state,
        ILoggerFactory? loggerFactory = null,
        [Description("Optional exact pattern name to filter to (matches UsageResult.TargetName for a finding, e.g. \"WCF\", \"WPF\", \"ConfigurationManager\", \"AppDomain\", \"COM Interop\" - see get_solution_overview's LegacyFindingsByPattern for the exact names in use). Omit to return findings for all patterns.")]
        string? patternName = null,
        CancellationToken cancellationToken = default)
    {
        var logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("DotNetModAssess.Mcp.Tools.FindingsTools");
        logger.LogDebug("Tool invoked: get_legacy_findings (patternName={PatternName})", patternName);
        await WorkspaceGuard.EnsureLoadedAsync(state, cancellationToken);

        IEnumerable<UsageResult> findings = state.LegacyFindings ?? [];

        if (!string.IsNullOrWhiteSpace(patternName))
        {
            findings = findings.Where(f => string.Equals(f.TargetName, patternName, StringComparison.OrdinalIgnoreCase));
        }

        return findings
            .OrderBy(f => f.TargetName, StringComparer.Ordinal)
            .ThenBy(f => f.FilePath, StringComparer.Ordinal)
            .ThenBy(f => f.LineNumber)
            .Select(DtoMapping.ToDto)
            .ToList();
    }
}
