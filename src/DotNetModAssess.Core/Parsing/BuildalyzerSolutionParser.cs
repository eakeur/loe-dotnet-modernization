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
    /// <summary>
    /// Upper bound on concurrent MSBuild evaluations. Buildalyzer/MSBuild evaluation is CPU- and
    /// I/O-heavy but each project's evaluation is independent of the others (evaluation, unlike
    /// the ProjectReference-linking pass below, doesn't need its referenced projects to already be
    /// built), so it's safe to fan out - bounded so a large monorepo doesn't spawn hundreds of
    /// concurrent MSBuild evaluations at once.
    /// </summary>
    private static readonly int MaxConcurrentEvaluations = Math.Max(2, Environment.ProcessorCount / 2);

    public async Task<SolutionModel> ParseAsync(string solutionPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Must happen before any Microsoft.Build.* type is touched, including by solution
        // discovery (which uses Microsoft.Build.Construction.SolutionFile to parse .sln files).
        MSBuildEnvironmentInitializer.EnsureRegistered();

        progress?.Report($"Discovering projects in {Path.GetFileName(solutionPath)}...");
        var discovered = SolutionFileDiscovery.Discover(solutionPath);
        var solutionRoot = Path.GetDirectoryName(discovered.SolutionPath)!;

        var manager = new AnalyzerManager();

        // Pre-warm the evaluation cache for every directly-discovered project in parallel (bounded
        // concurrency). The sequential graph-linking pass below then hits a warm cache for these
        // paths; only projects reachable *transitively* via a ProjectReference that isn't itself in
        // the .sln fall back to being evaluated lazily, one at a time, during linking.
        var totalProjects = discovered.Projects.Count;
        var evaluatedCount = 0;
        await Parallel.ForEachAsync(
            discovered.Projects,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentEvaluations, CancellationToken = cancellationToken },
            (project, ct) =>
            {
                EvaluationCache.GetOrEvaluate(manager, project.Path);
                var done = Interlocked.Increment(ref evaluatedCount);
                progress?.Report($"Evaluating {Path.GetFileName(project.Path)}... ({done}/{totalProjects})");
                return ValueTask.CompletedTask;
            });

        progress?.Report("Resolving project reference graph...");
        var builder = new SolutionGraphBuilder(manager, solutionRoot);

        foreach (var project in discovered.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.EnsureProjectBuilt(project.Path, project.ProjectTypeGuidRaw);
        }

        var projectModels = builder.GetAllBuiltProjectsInDiscoveryOrder(discovered.Projects.Select(p => p.Path));

        progress?.Report("Resolving NuGet sources and Central Package Management...");
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

        return solution;
    }
}
