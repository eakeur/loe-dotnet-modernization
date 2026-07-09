using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests.LegacyPatterns;

/// <summary>
/// Shared fixture-construction helpers for the <c>LegacyPatterns</c> detector tests, mirroring
/// <c>RoslynUsageScannerTests</c>'s own <c>CreateProject</c>/fixtures-root convention (see
/// <c>tests/DotNetModAssess.Core.Tests/UsageScanning/RoslynUsageScannerTests.cs</c>) so both test
/// suites agree on how a fixture "project" that isn't backed by a real .csproj is built.
/// </summary>
internal static class LegacyPatternsFixtureHelpers
{
    internal static string FixturesRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DotNetModAssess.sln")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new InvalidOperationException("Could not locate repo root (DotNetModAssess.sln) from test base directory.");
            }

            return Path.Combine(dir.FullName, "tests", "DotNetModAssess.Core.Tests", "LegacyPatternsFixtures");
        }
    }

    internal static ProjectModel CreateProject(
        string subfolder,
        string name,
        bool useWpf = false,
        IReadOnlyList<string>? comReferences = null)
    {
        var directory = Path.Combine(FixturesRoot, subfolder);

        return new ProjectModel
        {
            // The .csproj file itself need not exist -- detectors only walk the containing
            // directory for source/other files, per the same convention RoslynUsageScanner uses.
            Path = Path.Combine(directory, $"{name}.csproj"),
            Name = name,
            TargetFrameworks = ["net8.0"],
            Format = ProjectFormat.SdkStyle,
            OutputType = ProjectOutputType.Library,
            Metadata = new ProjectMetadata
            {
                RawProperties = new Dictionary<string, string?>(),
                Format = ProjectFormat.SdkStyle,
                PackagesModel = PackagesModel.PackageReference,
                TargetFrameworkRaw = "net8.0",
                TargetFrameworkClassification = TargetFrameworkClassification.Modern,
                RootNamespace = name,
                AssemblyName = name,
                UseWpf = useWpf,
                LegacySignals = new LegacyCouplingSignals
                {
                    ComReferences = comReferences ?? [],
                },
            },
        };
    }

    internal static SolutionModel BuildSolution(params ProjectModel[] projects) => new()
    {
        Path = Path.Combine(FixturesRoot, "SampleApp.sln"),
        Projects = projects,
    };
}
