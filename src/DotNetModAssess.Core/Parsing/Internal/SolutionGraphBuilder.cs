using Buildalyzer;
using DotNetModAssess.Core.Models;
using Microsoft.Extensions.Logging;

namespace DotNetModAssess.Core.Parsing.Internal;

/// <summary>
/// Builds the full graph of <see cref="ProjectModel"/> instances for a solution, resolving
/// ProjectReferences to sibling ProjectModel objects. Because ProjectModel is an immutable type
/// whose ProjectReferences list holds fully-built child models, projects are built in dependency
/// order (leaves first) via recursion with memoization. A project that is only reachable through
/// a ProjectReference (not part of the solution's own project list) is still discovered and built
/// on demand, matching "resolve ProjectReferences to sibling ProjectModels".
///
/// A ProjectReference pointing at a file that does not exist on disk (e.g. an intentionally
/// broken reference) is never treated as a hard failure for the *referencing* project: MSBuild
/// itself was observed to still evaluate such a project successfully in some cases (it does not
/// always validate P2P target existence at evaluation time), so instead we independently verify
/// each raw ProjectReference path ourselves and record an EvaluationDiagnostic on the referencing
/// project's Metadata.Diagnostics when it cannot be resolved, while simply omitting it from
/// ProjectReferences. This keeps the rest of the solution parsing correctly regardless of what a
/// given MSBuild/Buildalyzer combination happens to do with a dangling reference.
/// </summary>
internal sealed class SolutionGraphBuilder
{
    private readonly IAnalyzerManager _manager;
    private readonly string _solutionRoot;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, ProjectModel> _built = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _explicitTypeGuidsByPath = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> AllDirectoryBuildPropsFilesEncountered { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AllDirectoryBuildTargetsFilesEncountered { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SolutionGraphBuilder(IAnalyzerManager manager, string solutionRoot, ILogger? logger = null)
    {
        _manager = manager;
        _solutionRoot = solutionRoot;
        _logger = logger;
    }

    public void EnsureProjectBuilt(string projectPath, string? projectTypeGuidRaw)
    {
        if (!string.IsNullOrWhiteSpace(projectTypeGuidRaw))
        {
            _explicitTypeGuidsByPath[projectPath] = projectTypeGuidRaw;
        }

        Build(projectPath);
    }

    public IReadOnlyList<ProjectModel> GetAllBuiltProjectsInDiscoveryOrder(IEnumerable<string> discoveryOrderPaths)
    {
        var result = new List<ProjectModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in discoveryOrderPaths)
        {
            if (_built.TryGetValue(path, out var model) && seen.Add(path))
            {
                result.Add(model);
            }
        }

        // Projects pulled in only transitively (via ProjectReference) are appended after.
        foreach (var (path, model) in _built)
        {
            if (seen.Add(path))
            {
                result.Add(model);
            }
        }

        return result;
    }

    private ProjectModel? Build(string projectPath)
    {
        if (_built.TryGetValue(projectPath, out var existing))
        {
            return existing;
        }

        if (!File.Exists(projectPath))
        {
            return null;
        }

        if (!_inProgress.Add(projectPath))
        {
            // Circular ProjectReference - not valid MSBuild, but don't loop forever.
            return null;
        }

        try
        {
            return BuildInternal(projectPath);
        }
        finally
        {
            _inProgress.Remove(projectPath);
        }
    }

    private ProjectModel BuildInternal(string projectPath)
    {
        RawProjectFile rawFile;
        try
        {
            rawFile = RawProjectFile.Load(projectPath);
        }
        catch (Exception ex)
        {
            var stub = BuildUnparsableStub(projectPath, ex);
            _built[projectPath] = stub;
            return stub;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var directoryBuildPropsChain = DirectoryBuildFileWalker.FindDirectoryBuildFiles(projectDirectory, _solutionRoot, "Directory.Build.props");
        var directoryBuildTargetsChain = DirectoryBuildFileWalker.FindDirectoryBuildFiles(projectDirectory, _solutionRoot, "Directory.Build.targets");
        foreach (var f in directoryBuildPropsChain) AllDirectoryBuildPropsFilesEncountered.Add(f);
        foreach (var f in directoryBuildTargetsChain) AllDirectoryBuildTargetsFilesEncountered.Add(f);

        var evaluation = EvaluationCache.GetOrEvaluate(_manager, projectPath, _logger);

        var isCentrallyManaged = DetermineIsCentrallyManaged(evaluation, directoryBuildPropsChain);
        var nearestDirectoryPackagesProps = DirectoryBuildFileWalker.FindNearestDirectoryPackagesProps(projectDirectory, _solutionRoot);
        var cpmVersions = BuildCpmVersionLookup(evaluation, nearestDirectoryPackagesProps);

        var resolvedReferences = new List<ProjectModel>();
        var referenceDiagnostics = new List<EvaluationDiagnostic>();
        foreach (var rawRef in rawFile.ProjectReferences)
        {
            if (!File.Exists(rawRef.ResolvedPath))
            {
                referenceDiagnostics.Add(new EvaluationDiagnostic
                {
                    Severity = EvaluationDiagnosticSeverity.Error,
                    Code = "MissingProjectReference",
                    Message = $"ProjectReference '{rawRef.RawInclude}' could not be resolved: '{rawRef.ResolvedPath}' does not exist.",
                    File = projectPath,
                });
                continue;
            }

            var child = Build(rawRef.ResolvedPath);
            if (child is null)
            {
                referenceDiagnostics.Add(new EvaluationDiagnostic
                {
                    Severity = EvaluationDiagnosticSeverity.Error,
                    Code = "UnresolvedProjectReference",
                    Message = $"ProjectReference '{rawRef.RawInclude}' could not be included (circular reference or a load failure).",
                    File = projectPath,
                });
                continue;
            }

            resolvedReferences.Add(child);
        }

        var projectTypeGuids = ResolveProjectTypeGuids(projectPath, evaluation);

        var buildData = ProjectMetadataBuilder.Build(
            rawFile,
            evaluation,
            isCentrallyManaged,
            cpmVersions,
            directoryBuildPropsChain,
            directoryBuildTargetsChain,
            projectTypeGuids,
            referenceDiagnostics);

        var model = new ProjectModel
        {
            Path = projectPath,
            Name = Path.GetFileNameWithoutExtension(projectPath),
            TargetFrameworks = buildData.TargetFrameworks,
            Format = buildData.Format,
            OutputType = buildData.OutputType,
            ProjectReferences = resolvedReferences,
            PackageReferences = buildData.PackageReferences,
            Evaluation = evaluation.Succeeded
                ? ProjectEvaluationStatus.Ok()
                : ProjectEvaluationStatus.Failed(evaluation.ErrorMessage ?? "MSBuild evaluation failed for an unknown reason."),
            DirectoryBuildPropsChain = directoryBuildPropsChain,
            DirectoryBuildTargetsChain = directoryBuildTargetsChain,
            Metadata = buildData.Metadata,
        };

        _built[projectPath] = model;
        return model;
    }

    private static ProjectModel BuildUnparsableStub(string projectPath, Exception ex)
    {
        var metadata = new ProjectMetadata
        {
            RawProperties = new Dictionary<string, string?>(),
            Format = ProjectFormat.Unknown,
            TargetFrameworkRaw = string.Empty,
            Diagnostics =
            [
                new EvaluationDiagnostic
                {
                    Severity = EvaluationDiagnosticSeverity.Error,
                    Code = "ProjectFileUnreadable",
                    Message = $"Could not read/parse project file XML: {ex.Message}",
                    File = projectPath,
                },
            ],
        };

        return new ProjectModel
        {
            Path = projectPath,
            Name = Path.GetFileNameWithoutExtension(projectPath),
            TargetFrameworks = [],
            Format = ProjectFormat.Unknown,
            OutputType = ProjectOutputType.Unknown,
            Evaluation = ProjectEvaluationStatus.Failed($"Could not read project file '{projectPath}': {ex.Message}"),
            Metadata = metadata,
        };
    }

    private static bool DetermineIsCentrallyManaged(ProjectEvaluationOutcome evaluation, IReadOnlyList<string> directoryBuildPropsChain)
    {
        var evaluatedValue = evaluation.Result?.Properties.GetValueOrDefault("ManagePackageVersionsCentrally");
        if (!string.IsNullOrWhiteSpace(evaluatedValue) && bool.TryParse(evaluatedValue, out var parsed))
        {
            return parsed;
        }

        return DirectoryBuildFileWalker.ChainEnablesCentralPackageManagement(directoryBuildPropsChain);
    }

    private static IReadOnlyDictionary<string, string> BuildCpmVersionLookup(ProjectEvaluationOutcome evaluation, string? nearestDirectoryPackagesProps)
    {
        var result = nearestDirectoryPackagesProps is not null
            ? new Dictionary<string, string>(DirectoryBuildFileWalker.ParsePackageVersions(nearestDirectoryPackagesProps), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (evaluation.Result?.Items.TryGetValue("PackageVersion", out var items) == true)
        {
            foreach (var item in items)
            {
                if (item.Metadata.TryGetValue("Version", out var version) && !string.IsNullOrWhiteSpace(version))
                {
                    result[item.ItemSpec] = version;
                }
            }
        }

        return result;
    }

    private IReadOnlyList<Guid> ResolveProjectTypeGuids(string projectPath, ProjectEvaluationOutcome evaluation)
    {
        var guids = new List<Guid>();

        if (_explicitTypeGuidsByPath.TryGetValue(projectPath, out var slnTypeGuid) && Guid.TryParse(slnTypeGuid, out var parsedSlnGuid))
        {
            guids.Add(parsedSlnGuid);
        }

        var evaluatedTypeGuids = evaluation.Result?.Properties.GetValueOrDefault("ProjectTypeGuids");
        if (!string.IsNullOrWhiteSpace(evaluatedTypeGuids))
        {
            foreach (var segment in evaluatedTypeGuids.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Guid.TryParse(segment, out var parsed) && !guids.Contains(parsed))
                {
                    guids.Add(parsed);
                }
            }
        }

        return guids;
    }
}
