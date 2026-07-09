using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.UsageScanning;
using DotNetModAssess.Core.Workspaces;
using DotNetModAssess.Mcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetModAssess.Mcp.Tests.TestSupport;

/// <summary>
/// Builds a real, fully-wired <see cref="McpWorkspaceState"/> - the same real Core implementations
/// <c>Program.cs</c> registers (Buildalyzer parser, real graph builder, real Roslyn usage scanner,
/// all 5 real legacy-pattern detectors), just constructed directly rather than through a DI
/// container, since these tests have no need for one. Each call gets its own isolated
/// "recently opened" JSON file under the OS temp directory so tests never share/clobber a real
/// user's actual recent-workspaces list.
/// </summary>
internal static class WorkspaceStateFactory
{
    public static McpWorkspaceState Create(ISolutionParser? parserOverride = null)
    {
        var recentWorkspacesPath = Path.Combine(Path.GetTempPath(), $"dotnetmodassess-mcp-tests-recent-{Guid.NewGuid():N}.json");

        return new McpWorkspaceState(
            parserOverride ?? new BuildalyzerSolutionParser(),
            new DependencyGraphBuilder(),
            new RoslynUsageScanner(),
            new LegacyPatternScanner(
            [
                new WcfPatternDetector(),
                new WpfPatternDetector(),
                new ConfigurationManagerPatternDetector(),
                new AppDomainPatternDetector(),
                new ComInteropPatternDetector()
            ]),
            new JsonFileRecentWorkspacesStore(recentWorkspacesPath),
            NullLogger<McpWorkspaceState>.Instance);
    }

    /// <summary>Same as <see cref="Create"/>, but the parser is wrapped in a
    /// <see cref="GatedSolutionParser"/> so the caller can precisely control when loading is
    /// allowed to actually finish.</summary>
    public static (McpWorkspaceState State, GatedSolutionParser Gate) CreateGated()
    {
        var gate = new GatedSolutionParser(new BuildalyzerSolutionParser());
        var state = Create(gate);
        return (state, gate);
    }
}
