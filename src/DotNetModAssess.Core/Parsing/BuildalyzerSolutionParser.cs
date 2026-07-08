using Buildalyzer;
using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing.Internal;

namespace DotNetModAssess.Core.Parsing;

/// <summary>
/// Real <see cref="ISolutionParser"/> implementation built on Buildalyzer for MSBuild evaluation,
/// plus a set of evaluation-independent raw-XML readers (see Parsing/Internal) that keep format
/// detection, reference resolution, and legacy-signal detection working even when full MSBuild
/// evaluation fails for a given project (e.g. a legacy net48 project on a machine without a
/// full-framework MSBuild toolchain).
///
/// Per-project evaluation failures are always caught and recorded on that project's
/// <see cref="ProjectModel.Evaluation"/> rather than allowed to propagate, so a single bad project
/// never aborts the scan of the rest of the solution.
/// </summary>
public sealed class BuildalyzerSolutionParser : ISolutionParser
{
    public Task<SolutionModel> ParseAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Must happen before any Microsoft.Build.* type is touched, including by solution
        // discovery (which uses Microsoft.Build.Construction.SolutionFile to parse .sln files).
        MSBuildEnvironmentInitializer.EnsureRegistered();

        var discovered = SolutionFileDiscovery.Discover(solutionPath);
        var solutionRoot = Path.GetDirectoryName(discovered.SolutionPath)!;

        var manager = new AnalyzerManager();
        var builder = new SolutionGraphBuilder(manager, solutionRoot);

        foreach (var project in discovered.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.EnsureProjectBuilt(project.Path, project.ProjectTypeGuidRaw);
        }

        var projectModels = builder.GetAllBuiltProjectsInDiscoveryOrder(discovered.Projects.Select(p => p.Path));

        var directoryPackagesPropsPath = DirectoryBuildFileWalker.FindNearestDirectoryPackagesProps(solutionRoot, solutionRoot);
        var nugetSources = NuGetConfigResolver.ResolveSources(solutionRoot);

        var solution = new SolutionModel
        {
            Path = discovered.SolutionPath,
            Projects = projectModels,
            NuGetSources = nugetSources,
            DirectoryBuildPropsFiles = builder.AllDirectoryBuildPropsFilesEncountered.ToList(),
            DirectoryBuildTargetsFiles = builder.AllDirectoryBuildTargetsFilesEncountered.ToList(),
            DirectoryPackagesPropsPath = directoryPackagesPropsPath,
        };

        return Task.FromResult(solution);
    }
}
