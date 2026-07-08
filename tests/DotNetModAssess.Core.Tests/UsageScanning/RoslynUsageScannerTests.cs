using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.UsageScanning;

namespace DotNetModAssess.Core.Tests.UsageScanning;

public class RoslynUsageScannerTests
{
    private static string FixturesRoot
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

            return Path.Combine(dir.FullName, "tests", "DotNetModAssess.Core.Tests", "UsageScanningFixtures");
        }
    }

    private static ProjectModel CreateProject(
        string subfolder,
        string name,
        string rootNamespace,
        IReadOnlyList<PackageReferenceModel>? packageReferences = null)
    {
        var directory = Path.Combine(FixturesRoot, subfolder);

        return new ProjectModel
        {
            // The .csproj file itself need not exist -- the scanner only walks the
            // containing directory for *.cs files, per the convention documented on
            // RoslynUsageScanner.
            Path = Path.Combine(directory, $"{name}.csproj"),
            Name = name,
            TargetFrameworks = ["net8.0"],
            Format = ProjectFormat.SdkStyle,
            OutputType = ProjectOutputType.Library,
            PackageReferences = packageReferences ?? [],
            Metadata = new ProjectMetadata
            {
                RawProperties = new Dictionary<string, string?>(),
                Format = ProjectFormat.SdkStyle,
                PackagesModel = PackagesModel.PackageReference,
                TargetFrameworkRaw = "net8.0",
                TargetFrameworkClassification = TargetFrameworkClassification.Modern,
                RootNamespace = rootNamespace,
                AssemblyName = name,
            },
        };
    }

    private static SolutionModel BuildSolution()
    {
        var sharedProject = CreateProject("SharedProject", "SampleApp.Shared", "SampleApp.Shared");

        var legacyProject = CreateProject(
            "LegacyServiceProject",
            "SampleApp.LegacyService",
            "SampleApp.LegacyService",
            packageReferences:
            [
                new PackageReferenceModel { PackageId = "System.Web" },
                new PackageReferenceModel { PackageId = "System.ServiceModel" },
            ]);

        var webProject = CreateProject("WebProject", "SampleApp.Web", "SampleApp.Web");

        return new SolutionModel
        {
            Path = Path.Combine(FixturesRoot, "SampleApp.sln"),
            Projects = [sharedProject, legacyProject, webProject],
        };
    }

    private static Task<IReadOnlyList<UsageResult>> ScanAsync() =>
        new RoslynUsageScanner().ScanAsync(BuildSolution());

    [Fact]
    public async Task ScanAsync_DetectsUsingDirectivesForPackageAndProjectTargets()
    {
        var results = await ScanAsync();

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.UsingDirective &&
            r.MatchedSymbol == "System.Web" &&
            r.FilePath.EndsWith("OrderNotifier.cs", StringComparison.Ordinal));

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.UsingDirective &&
            r.MatchedSymbol == "System.ServiceModel" &&
            r.FilePath.EndsWith("OrderNotifier.cs", StringComparison.Ordinal));

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.UsingDirective &&
            r.MatchedSymbol == "SampleApp.Shared" &&
            r.FilePath.EndsWith("Program.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_DetectsFullyQualifiedTypeReferences()
    {
        var results = await ScanAsync();

        // `SampleApp.Shared.OrderDto order = new SampleApp.Shared.OrderDto();` in Program.cs
        // should yield two TypeReference matches: the variable's declared type and the
        // object-creation type.
        var typeRefs = results
            .Where(r => r.Kind == UsageReferenceKind.TypeReference &&
                        r.MatchedSymbol == "SampleApp.Shared.OrderDto" &&
                        r.FilePath.EndsWith("Program.cs", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, typeRefs.Count);
        Assert.All(typeRefs, r => Assert.Equal(10, r.LineNumber));
        Assert.All(typeRefs, r => Assert.EndsWith("SampleApp.Web.csproj", r.ProjectPath));
    }

    [Fact]
    public async Task ScanAsync_DetectsAttributeAndBaseTypeReferences()
    {
        var results = await ScanAsync();

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "SampleApp.Shared.LegacyMarkerAttribute" &&
            r.FilePath.EndsWith("OrderNotifier.cs", StringComparison.Ordinal));

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.BaseTypeOrInterface &&
            r.MatchedSymbol == "SampleApp.Shared.INotifier" &&
            r.FilePath.EndsWith("OrderNotifier.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_DetectsMemberAccessExpressionsRootedAtQualifiedTypes()
    {
        var results = await ScanAsync();

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "System.Web.HttpContext" &&
            r.FilePath.EndsWith("OrderNotifier.cs", StringComparison.Ordinal));

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "System.ServiceModel.OperationContext" &&
            r.FilePath.EndsWith("OrderNotifier.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_ReportsCorrectFilePathLineNumberAndProjectPath()
    {
        var results = await ScanAsync();

        var usingWebResult = Assert.Single(results, r =>
            r.Kind == UsageReferenceKind.UsingDirective && r.MatchedSymbol == "System.Web");

        Assert.Equal(2, usingWebResult.LineNumber); // `using System.Web;` is the 2nd line in OrderNotifier.cs
        Assert.True(File.Exists(usingWebResult.FilePath));
        Assert.EndsWith("SampleApp.LegacyService.csproj", usingWebResult.ProjectPath);
        Assert.Equal("using System.Web;", usingWebResult.CodeSnippet);
    }

    [Fact]
    public async Task ScanAsync_UnrelatedFileProducesNoMatches()
    {
        var results = await ScanAsync();

        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("Unrelated.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_DoesNotReportAProjectUsingItsOwnNamespace()
    {
        var results = await ScanAsync();

        // SelfReference.cs (inside SampleApp.Shared itself) fully qualifies
        // SampleApp.Shared.OrderDto twice -- but since SampleApp.Shared is that very
        // project's own root namespace, it must be excluded as a self-match.
        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("SelfReference.cs", StringComparison.Ordinal));
    }
}
