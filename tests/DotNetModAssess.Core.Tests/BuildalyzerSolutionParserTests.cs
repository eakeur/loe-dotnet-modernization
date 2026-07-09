using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.Parsing.Internal;

namespace DotNetModAssess.Core.Tests;

/// <summary>
/// Exercises the real Buildalyzer-based solution parser against fixtures/SampleLegacySolution, a
/// hand-built solution containing:
///   - Legacy.Net48App: a legacy non-SDK-style net48 project using packages.config
///   - Modern.Sdk: a modern net8.0 SDK-style project that
///       * references Legacy.Net48App (crossing TFMs, modeling in-progress modernization)
///       * references a nonexistent project file (DoesNotExist.csproj) to exercise graceful
///         failure handling
///       * consumes Newtonsoft.Json via Central Package Management (Directory.Packages.props)
///
/// Per the module's own documented constraint, this repo is developed on macOS, which has no
/// full-framework MSBuild/VS Build Tools toolchain. Buildalyzer evaluation of the legacy net48
/// project may therefore either succeed (if a compatible toolchain such as Mono's msbuild is
/// picked up) or fail gracefully - both are acceptable outcomes here. What must always hold is:
/// no unhandled exception is thrown, and the rest of the solution keeps parsing correctly.
/// </summary>
public class BuildalyzerSolutionParserTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DotNetModAssess.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root from test base directory.");
        }
    }

    private static string FixtureSolutionPath =>
        Path.Combine(RepoRoot, "fixtures", "SampleLegacySolution", "SampleLegacySolution.sln");

    private static string FixtureFilterPath =>
        Path.Combine(RepoRoot, "fixtures", "SampleLegacySolution", "ModernOnly.slnf");

    private static string FixtureStandaloneProjectPath =>
        Path.Combine(RepoRoot, "fixtures", "SampleLegacySolution", "Modern.Sdk", "Modern.Sdk.csproj");

    private static async Task<SolutionModel> ParseFixtureAsync()
    {
        ISolutionParser parser = new BuildalyzerSolutionParser();
        return await parser.ParseAsync(FixtureSolutionPath);
    }

    [Fact]
    public async Task ParseAsync_DoesNotThrow_AndReturnsBothProjects()
    {
        var solution = await ParseFixtureAsync();

        Assert.Equal(2, solution.Projects.Count);
        Assert.Contains(solution.Projects, p => p.Name == "Modern.Sdk");
        Assert.Contains(solution.Projects, p => p.Name == "Legacy.Net48App");
    }

    [Fact]
    public async Task ParseAsync_DetectsSdkStyleVsLegacyStyle_FromRawXml_NotFromEvaluationOutcome()
    {
        var solution = await ParseFixtureAsync();

        var modern = solution.Projects.Single(p => p.Name == "Modern.Sdk");
        var legacy = solution.Projects.Single(p => p.Name == "Legacy.Net48App");

        Assert.Equal(ProjectFormat.SdkStyle, modern.Format);
        Assert.Equal(ProjectFormat.LegacyStyle, legacy.Format);

        // Format must reflect the raw XML regardless of whether evaluation itself succeeded.
        Assert.Equal(modern.Format, modern.Metadata.Format);
        Assert.Equal(legacy.Format, legacy.Metadata.Format);
    }

    [Fact]
    public async Task ParseAsync_DetectsTargetFrameworks_ForBothModernAndLegacyProjects()
    {
        var solution = await ParseFixtureAsync();

        var modern = solution.Projects.Single(p => p.Name == "Modern.Sdk");
        var legacy = solution.Projects.Single(p => p.Name == "Legacy.Net48App");

        Assert.Equal(["net8.0"], modern.TargetFrameworks);
        Assert.Equal(TargetFrameworkClassification.Modern, modern.Metadata.TargetFrameworkClassification);

        // Legacy TargetFrameworkVersion "v4.8" is normalized to the "net48" moniker.
        Assert.Equal(["net48"], legacy.TargetFrameworks);
        Assert.Equal(TargetFrameworkClassification.NetFramework, legacy.Metadata.TargetFrameworkClassification);
    }

    [Fact]
    public async Task ParseAsync_ResolvesRealProjectReference_ToTheSameSiblingProjectModelInstance()
    {
        var solution = await ParseFixtureAsync();

        var modern = solution.Projects.Single(p => p.Name == "Modern.Sdk");
        var legacy = solution.Projects.Single(p => p.Name == "Legacy.Net48App");

        var reference = Assert.Single(modern.ProjectReferences);
        Assert.Same(legacy, reference);
    }

    [Fact]
    public async Task ParseAsync_HandlesBrokenProjectReference_Gracefully_WithoutThrowing()
    {
        // The mere fact that this completes (called from every other test too) demonstrates the
        // parser does not throw because of the broken ProjectReference. This test additionally
        // asserts the failure is surfaced rather than silently swallowed.
        var solution = await ParseFixtureAsync();
        var modern = solution.Projects.Single(p => p.Name == "Modern.Sdk");

        // The broken reference must not appear as a resolved sibling project.
        Assert.DoesNotContain(modern.ProjectReferences, p => p.Name == "DoesNotExist");

        // But it must be visibly reported as a diagnostic, with the project path included.
        var diagnostic = Assert.Single(modern.Metadata.Diagnostics, d => d.Code == "MissingProjectReference");
        Assert.Contains("DoesNotExist", diagnostic.Message);
        Assert.Equal(EvaluationDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task ParseAsync_LegacyProject_EvaluatesOrFailsGracefully_ButNeverThrows_AndRestOfSolutionStillParses()
    {
        var solution = await ParseFixtureAsync();
        var legacy = solution.Projects.Single(p => p.Name == "Legacy.Net48App");
        var modern = solution.Projects.Single(p => p.Name == "Modern.Sdk");

        // Either outcome is acceptable on this machine - what matters is it is one of the two
        // well-defined ProjectEvaluationStatus shapes, and that the modern project (evaluated
        // independently) still came back with a successful evaluation.
        if (!legacy.Evaluation.Succeeded)
        {
            Assert.False(string.IsNullOrWhiteSpace(legacy.Evaluation.ErrorMessage));
        }

        Assert.True(modern.Evaluation.Succeeded, "Modern.Sdk should evaluate successfully regardless of the legacy project's outcome.");
    }

    [Fact]
    public async Task ParseAsync_ResolvesCentrallyManagedPackageVersion_ForModernProject()
    {
        var solution = await ParseFixtureAsync();
        var modern = solution.Projects.Single(p => p.Name == "Modern.Sdk");

        var newtonsoft = Assert.Single(modern.PackageReferences, p => p.PackageId == "Newtonsoft.Json");

        Assert.True(newtonsoft.IsCentrallyManaged);
        Assert.Null(newtonsoft.RequestedVersion); // bare <PackageReference Include="Newtonsoft.Json" />
        Assert.Equal("13.0.3", newtonsoft.ResolvedVersion); // from Directory.Packages.props
        Assert.True(modern.Metadata.IsCentrallyManaged);
    }

    [Fact]
    public async Task ParseAsync_ResolvesPackagesConfigEntry_ForLegacyProject()
    {
        var solution = await ParseFixtureAsync();
        var legacy = solution.Projects.Single(p => p.Name == "Legacy.Net48App");

        Assert.Equal(PackagesModel.PackagesConfig, legacy.Metadata.PackagesModel);

        var newtonsoft = Assert.Single(legacy.PackageReferences, p => p.PackageId == "Newtonsoft.Json");
        Assert.False(newtonsoft.IsCentrallyManaged);
        Assert.Equal("13.0.3", newtonsoft.RequestedVersion);
        Assert.Equal("13.0.3", newtonsoft.ResolvedVersion);
    }

    [Fact]
    public async Task ParseAsync_DetectsLegacyCouplingSignals_ForLegacyProject()
    {
        var solution = await ParseFixtureAsync();
        var legacy = solution.Projects.Single(p => p.Name == "Legacy.Net48App");

        var signals = legacy.Metadata.LegacySignals;
        Assert.True(signals.ReferencesSystemWeb);
        Assert.True(signals.ReferencesSystemServiceModel);
        Assert.True(signals.HasAppConfig);
        Assert.Contains("System.Web", signals.LegacyAssemblyReferences);
    }

    [Fact]
    public async Task ParseAsync_PopulatesDirectoryBuildPropsChain_ForBothProjects()
    {
        var solution = await ParseFixtureAsync();
        var modern = solution.Projects.Single(p => p.Name == "Modern.Sdk");

        var rootDirectoryBuildProps = Path.Combine(RepoRoot, "fixtures", "SampleLegacySolution", "Directory.Build.props");
        Assert.Contains(modern.DirectoryBuildPropsChain, f => string.Equals(f, rootDirectoryBuildProps, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(solution.DirectoryPackagesPropsPath);
        Assert.True(solution.IsCentrallyManaged);
    }

    [Fact]
    public void SolutionFileDiscovery_ParsesSln_AndReturnsBothProjectPaths()
    {
        var discovered = SolutionFileDiscovery.Discover(FixtureSolutionPath);

        Assert.Equal(2, discovered.Projects.Count);
        Assert.Contains(discovered.Projects, p => p.Path.EndsWith("Modern.Sdk.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(discovered.Projects, p => p.Path.EndsWith("Legacy.Net48App.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SolutionFileDiscovery_ParsesSlnf_AndAppliesProjectFilter()
    {
        var discovered = SolutionFileDiscovery.Discover(FixtureFilterPath);

        var project = Assert.Single(discovered.Projects);
        Assert.EndsWith("Modern.Sdk.csproj", project.Path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("SampleLegacySolution.sln", discovered.SolutionPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolutionFileDiscovery_ParsesStandaloneProjectFile_AsASingleProjectSolution()
    {
        var discovered = SolutionFileDiscovery.Discover(FixtureStandaloneProjectPath);

        var project = Assert.Single(discovered.Projects);
        Assert.EndsWith("Modern.Sdk.csproj", project.Path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Modern.Sdk.csproj", discovered.SolutionPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolutionFileDiscovery_RejectsUnsupportedExtension()
    {
        Assert.Throws<NotSupportedException>(() => SolutionFileDiscovery.Discover("SomeFile.txt"));
    }

    [Fact]
    public async Task ParseAsync_StandaloneProjectFile_StillResolvesItsProjectReferenceTransitively()
    {
        ISolutionParser parser = new BuildalyzerSolutionParser();
        var solution = await parser.ParseAsync(FixtureStandaloneProjectPath);

        // Only Modern.Sdk.csproj was handed to the parser - no .sln at all - but its
        // ProjectReference to Legacy.Net48App must still be discovered and built on demand (see
        // SolutionGraphBuilder's own doc comment), the same as when parsing via the .sln.
        Assert.Equal(2, solution.Projects.Count);
        var modern = Assert.Single(solution.Projects, p => p.Name == "Modern.Sdk");
        Assert.Contains(solution.Projects, p => p.Name == "Legacy.Net48App");
        Assert.Contains(modern.ProjectReferences, r => r.Name == "Legacy.Net48App");
    }

    [Theory]
    [InlineData("v4.8", "net48")]
    [InlineData("v4.7.2", "net472")]
    [InlineData("v4.6.1", "net461")]
    [InlineData("net8.0", "net8.0")]
    [InlineData("netstandard2.0", "netstandard2.0")]
    public void TargetFrameworkClassifier_NormalizesLegacyVersionsToMonikers(string raw, string expected)
    {
        Assert.Equal(expected, TargetFrameworkClassifier.NormalizeToMoniker(raw));
    }

    /// <summary>
    /// Regression test for SolutionGraphBuilder's own cycle guard (the `_inProgress` HashSet in
    /// Parsing/Internal/SolutionGraphBuilder.cs): a genuine A-references-B-references-A
    /// ProjectReference cycle (invalid for an actual MSBuild build, but nothing stops someone from
    /// authoring one) must never hang the parser - it should return promptly with both projects
    /// present, regardless of whether Buildalyzer's own MSBuild evaluation of the cyclic pair
    /// succeeds or fails (that's orthogonal to what's being proven here). The explicit Timeout
    /// means this test fails loudly rather than hanging the whole run if that guard is ever
    /// accidentally removed/broken.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task ParseAsync_HandlesCircularProjectReferences_WithoutHanging()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CycleSafetyTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var projectAPath = Path.Combine(dir, "ProjectA.csproj");
        var projectBPath = Path.Combine(dir, "ProjectB.csproj");

        try
        {
            File.WriteAllText(projectAPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="ProjectB.csproj" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(projectBPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="ProjectA.csproj" />
                  </ItemGroup>
                </Project>
                """);

            ISolutionParser parser = new BuildalyzerSolutionParser();
            var solution = await parser.ParseAsync(projectAPath);

            Assert.Equal(2, solution.Projects.Count);
            Assert.Contains(solution.Projects, p => p.Name == "ProjectA");
            Assert.Contains(solution.Projects, p => p.Name == "ProjectB");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Regression test for a real bug: per the .slnf format spec, only "solution.path" is relative
    /// to the .slnf file's own directory - every entry in "solution.projects" is relative to the
    /// SOLUTION's directory instead (matching how those same paths appear inside the .sln itself),
    /// regardless of where the .slnf physically lives. The existing ModernOnly.slnf fixture sits
    /// right next to SampleLegacySolution.sln, so it can't catch a resolve-projects-relative-to-
    /// filter-directory bug - this test deliberately puts the .slnf in a *different* directory
    /// (a sibling "filters" folder) to prove project paths still resolve correctly.
    /// </summary>
    [Fact]
    public void SolutionFileDiscovery_ParsesSlnf_WhenFilterLivesInADifferentDirectoryThanTheSolution()
    {
        var root = Path.Combine(Path.GetTempPath(), "SlnfDifferentDirTests_" + Guid.NewGuid());
        var solutionDir = Path.Combine(root, "solution");
        var filtersDir = Path.Combine(root, "filters");
        Directory.CreateDirectory(solutionDir);
        Directory.CreateDirectory(filtersDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(solutionDir, "ProjectA"));
            Directory.CreateDirectory(Path.Combine(solutionDir, "ProjectB"));
            File.WriteAllText(Path.Combine(solutionDir, "ProjectA", "ProjectA.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(solutionDir, "ProjectB", "ProjectB.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);

            var solutionPath = Path.Combine(solutionDir, "Test.sln");
            File.WriteAllText(solutionPath, """
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.0.31903.59
                MinimumVisualStudioVersion = 10.0.40219.1
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ProjectA", "ProjectA\ProjectA.csproj", "{D521F944-D387-43E5-9B32-C1A81FEC9868}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ProjectB", "ProjectB\ProjectB.csproj", "{2E1F3B0A-9B7C-4B7B-8F1B-1F5A3D6C7E10}"
                EndProject
                Global
                	GlobalSection(SolutionConfigurationPlatforms) = preSolution
                		Debug|Any CPU = Debug|Any CPU
                	EndGlobalSection
                	GlobalSection(ProjectConfigurationPlatforms) = postSolution
                		{D521F944-D387-43E5-9B32-C1A81FEC9868}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{2E1F3B0A-9B7C-4B7B-8F1B-1F5A3D6C7E10}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                	EndGlobalSection
                EndGlobal
                """);

            // The filter lives in a sibling directory, not next to the .sln - "path" points up and
            // back into solutionDir, while "projects" entries are relative to solutionDir itself
            // (NOT to this filter file's own directory, which is filtersDir).
            var filterPath = Path.Combine(filtersDir, "OnlyA.slnf");
            File.WriteAllText(filterPath, """
                {
                  "solution": {
                    "path": "..\\solution\\Test.sln",
                    "projects": [
                      "ProjectA\\ProjectA.csproj"
                    ]
                  }
                }
                """);

            var discovered = SolutionFileDiscovery.Discover(filterPath);

            var project = Assert.Single(discovered.Projects);
            Assert.EndsWith("ProjectA.csproj", project.Path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
