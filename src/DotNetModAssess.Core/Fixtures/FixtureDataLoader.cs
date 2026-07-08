using System.Text.Json;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Fixtures;

/// <summary>
/// Hydrates the flat, string-keyed Fixture*Dto JSON files into real domain object graphs
/// (SolutionModel/DependencyGraph with actual nested references), for use in tests until
/// a real ISolutionParser/IDependencyGraphBuilder implementation exists.
/// </summary>
public static class FixtureDataLoader
{
    public static SolutionModel LoadSolution(string solutionJsonPath)
    {
        var dto = JsonSerializer.Deserialize<FixtureSolutionDto>(File.ReadAllText(solutionJsonPath), FixtureJsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to deserialize {solutionJsonPath}");
        return HydrateSolution(dto);
    }

    public static SolutionModel HydrateSolution(FixtureSolutionDto dto)
    {
        var byPath = dto.Projects.ToDictionary(p => p.Path);
        var built = new Dictionary<string, ProjectModel>();

        // Project references form a DAG (MSBuild disallows cycles), so a memoized
        // recursive build guarantees every reference points at the one final,
        // fully-populated instance for that path - never a half-built placeholder.
        ProjectModel Build(string path)
        {
            if (built.TryGetValue(path, out var existing))
            {
                return existing;
            }

            var p = byPath[path];
            var project = new ProjectModel
            {
                Path = p.Path,
                Name = p.Name,
                TargetFrameworks = p.TargetFrameworks,
                Format = ParseEnum<ProjectFormat>(p.Format),
                OutputType = ParseEnum<ProjectOutputType>(p.OutputType),
                ProjectReferences = p.ProjectReferencePaths.Select(Build).ToList(),
                PackageReferences = p.PackageReferences.Select(BuildPackageReference).ToList(),
                Evaluation = p.Evaluation.Succeeded
                    ? ProjectEvaluationStatus.Ok()
                    : ProjectEvaluationStatus.Failed(p.Evaluation.ErrorMessage ?? "Evaluation failed"),
                DirectoryBuildPropsChain = p.DirectoryBuildPropsChain,
                DirectoryBuildTargetsChain = p.DirectoryBuildTargetsChain,
                Metadata = BuildMetadata(p.Metadata)
            };
            built[path] = project;
            return project;
        }

        return new SolutionModel
        {
            Path = dto.Path,
            Projects = dto.Projects.Select(p => Build(p.Path)).ToList(),
            NuGetSources = dto.NuGetSources.Select(s => new NuGetSourceModel
            {
                Name = s.Name,
                Url = s.Url,
                IsEnabled = s.IsEnabled,
                ConfigFilePath = s.ConfigFilePath
            }).ToList(),
            DirectoryBuildPropsFiles = dto.DirectoryBuildPropsFiles,
            DirectoryBuildTargetsFiles = dto.DirectoryBuildTargetsFiles,
            DirectoryPackagesPropsPath = dto.DirectoryPackagesPropsPath
        };
    }

    public static DependencyGraph LoadGraph(string graphJsonPath, SolutionModel solution)
    {
        var dto = JsonSerializer.Deserialize<FixtureGraphDto>(File.ReadAllText(graphJsonPath), FixtureJsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to deserialize {graphJsonPath}");
        return HydrateGraph(dto, solution);
    }

    public static DependencyGraph HydrateGraph(FixtureGraphDto dto, SolutionModel solution)
    {
        var projectsByPath = solution.Projects.ToDictionary(p => p.Path);

        var nodes = dto.Nodes.Select(n => n.Kind switch
        {
            "Project" => (GraphNode)new ProjectGraphNode { Project = projectsByPath[n.Id] },
            "Package" => new PackageGraphNode
            {
                Package = new PackageModel
                {
                    PackageId = n.Id,
                    ProjectVersions = (n.ProjectVersions ?? [])
                        .Select(pv => new PackageProjectVersion(pv.ProjectPath, pv.Version))
                        .ToList(),
                    HasVersionConflict = n.HasVersionConflict ?? false
                }
            },
            _ => throw new InvalidOperationException($"Unknown graph node kind '{n.Kind}'")
        }).ToList();

        var edges = dto.Edges.Select(e => new GraphEdge
        {
            Kind = ParseEnum<GraphEdgeKind>(e.Kind),
            FromId = e.FromId,
            ToId = e.ToId,
            ResolvedPackageVersion = e.ResolvedPackageVersion
        }).ToList();

        return new DependencyGraph { Nodes = nodes, Edges = edges };
    }

    public static IReadOnlyList<UsageResult> LoadUsageResults(string usageResultsJsonPath) =>
        JsonSerializer.Deserialize<List<UsageResult>>(File.ReadAllText(usageResultsJsonPath), FixtureJsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to deserialize {usageResultsJsonPath}");

    private static PackageReferenceModel BuildPackageReference(FixturePackageReferenceDto p) => new()
    {
        PackageId = p.PackageId,
        RequestedVersion = p.RequestedVersion,
        ResolvedVersion = p.ResolvedVersion,
        IsCentrallyManaged = p.IsCentrallyManaged,
        IsFloatingVersion = p.IsFloatingVersion
    };

    private static ProjectMetadata BuildMetadata(FixtureProjectMetadataDto m) => new()
    {
        RawProperties = m.RawProperties,
        Format = ParseEnum<ProjectFormat>(m.Format),
        PackagesModel = ParseEnum<PackagesModel>(m.PackagesModel),
        IsCentrallyManaged = m.IsCentrallyManaged,
        TargetFrameworkRaw = m.TargetFrameworkRaw,
        TargetFrameworkClassification = ParseEnum<TargetFrameworkClassification>(m.TargetFrameworkClassification),
        OutputType = ParseEnum<ProjectOutputType>(m.OutputType),
        ProjectTypeGuids = m.ProjectTypeGuids.Select(Guid.Parse).ToList(),
        LangVersion = m.LangVersion,
        Nullable = m.Nullable,
        ImplicitUsings = m.ImplicitUsings,
        AllowUnsafeBlocks = m.AllowUnsafeBlocks,
        TreatWarningsAsErrors = m.TreatWarningsAsErrors,
        DefineConstants = m.DefineConstants,
        AssemblyName = m.AssemblyName,
        RootNamespace = m.RootNamespace,
        RuntimeIdentifiers = m.RuntimeIdentifiers,
        PlatformTarget = m.PlatformTarget,
        UseWindowsForms = m.UseWindowsForms,
        UseWpf = m.UseWpf,
        IsPackable = m.IsPackable,
        LegacySignals = BuildLegacySignals(m.LegacySignals),
        DirectoryBuildPropsChain = m.DirectoryBuildPropsChain,
        DirectoryBuildTargetsChain = m.DirectoryBuildTargetsChain,
        IsTestProject = m.IsTestProject,
        TestFramework = m.TestFramework,
        Diagnostics = (m.Diagnostics ?? [])
            .Select(d => new EvaluationDiagnostic
            {
                Severity = ParseEnum<EvaluationDiagnosticSeverity>(d.Severity),
                Code = d.Code,
                Message = d.Message,
                File = d.File,
                LineNumber = d.LineNumber
            }).ToList()
    };

    private static LegacyCouplingSignals BuildLegacySignals(FixtureLegacyCouplingSignalsDto s) => new()
    {
        ReferencesSystemWeb = s.ReferencesSystemWeb,
        ReferencesSystemServiceModel = s.ReferencesSystemServiceModel,
        ReferencesSystemMessaging = s.ReferencesSystemMessaging,
        ComReferences = s.ComReferences,
        HasPInvokeSignals = s.HasPInvokeSignals,
        HasAppConfig = s.HasAppConfig,
        HasWebConfig = s.HasWebConfig,
        UsesConfigurationManager = s.UsesConfigurationManager,
        LegacyAssemblyReferences = s.LegacyAssemblyReferences,
        HasWebConfigTransforms = s.HasWebConfigTransforms,
        WindowsOnlyAssemblyReferences = s.WindowsOnlyAssemblyReferences
    };

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(value, ignoreCase: true);
}
