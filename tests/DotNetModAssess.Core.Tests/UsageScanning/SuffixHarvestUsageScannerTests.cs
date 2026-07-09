using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.UsageScanning;

namespace DotNetModAssess.Core.Tests.UsageScanning;

/// <summary>
/// Exercises the third, suffix-harvest usage-detection pass that <see cref="RoslynUsageScanner"/>
/// unions into its results (see that class's own doc comment for the three-pass architecture, and
/// the internal <c>SuffixHarvestUsageScanner</c> class for the harvesting/search rules). Uses a
/// dedicated two-project fixture (under "UsageScanningFixtures/HarvestSourceProject" and
/// "UsageScanningFixtures/HarvestConsumerProject") so the harvest source (fully-qualified confirmed
/// references) and the harvest consumer (later bare references) are clearly separated, mirroring
/// the real-world shape of the bug this pass exists to catch.
/// </summary>
public class SuffixHarvestUsageScannerTests
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
            // The .csproj file itself need not exist -- the scanner only walks the containing
            // directory for *.cs files, per the convention documented on RoslynUsageScanner.
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
        var sourceProject = CreateProject(
            "HarvestSourceProject",
            "HarvestSample.Source",
            "HarvestSource",
            packageReferences:
            [
                new PackageReferenceModel { PackageId = "System.Web" },
                new PackageReferenceModel { PackageId = "Acme.Utilities" },
            ]);

        var consumerProject = CreateProject("HarvestConsumerProject", "HarvestSample.Consumer", "HarvestConsumer");

        return new SolutionModel
        {
            Path = Path.Combine(FixturesRoot, "HarvestSample.sln"),
            Projects = [sourceProject, consumerProject],
        };
    }

    private static Task<IReadOnlyList<UsageResult>> ScanAsync() =>
        new RoslynUsageScanner().ScanAsync(BuildSolution());

    [Fact]
    public async Task ScanAsync_FindsBareIdentifierHarvestedFromAnUnrelatedConfirmedReference()
    {
        var results = await ScanAsync();

        // BareUsage.cs line 15 ("var ctx = HttpContext.Current;") has no using directive on that
        // line and no fully-qualified text -- Roslyn structurally can't confirm it, and the plain
        // text-search pass can't find the literal "System.Web" text there either. Only the
        // suffix-harvest pass, seeded by HarvestSourceProject/Source.cs's unrelated confirmed
        // "System.Web.HttpContext" reference, can catch it.
        var match = Assert.Single(results, r =>
            r.FilePath.EndsWith("BareUsage.cs", StringComparison.Ordinal) &&
            r.LineNumber == 15);

        Assert.Equal(UsageConfidence.SuffixMatch, match.Confidence);
        Assert.Equal("System.Web", match.TargetName);
        Assert.Equal("HttpContext", match.MatchedSymbol);
        Assert.Equal(UsageReferenceKind.Other, match.Kind);
    }

    [Fact]
    public async Task ScanAsync_UsingDirectiveOnEarlierLineStaysConfirmedAndSeparateFromTheBareMatch()
    {
        var results = await ScanAsync();

        // BareUsage.cs line 2 ("using System.Web;") is a completely separate occurrence from the
        // bare "HttpContext.Current" reference several lines later -- it must stay Confirmed and
        // must not be conflated with (or suppress/duplicate) the SuffixMatch result on line 15.
        var usingResult = Assert.Single(results, r =>
            r.FilePath.EndsWith("BareUsage.cs", StringComparison.Ordinal) &&
            r.LineNumber == 2);

        Assert.Equal(UsageConfidence.Confirmed, usingResult.Confidence);
        Assert.Equal(UsageReferenceKind.UsingDirective, usingResult.Kind);
        Assert.Equal("System.Web", usingResult.TargetName);
    }

    [Fact]
    public async Task ScanAsync_ScopesHarvestedSuffixesPerTargetWithoutCrossAttribution()
    {
        var results = await ScanAsync();

        // BareLogger.cs's bare "Logger" reference was harvested from a *different* confirmed
        // reference ("Acme.Utilities.Logger") than BareUsage.cs's "HttpContext" was
        // ("System.Web.HttpContext"). It must be attributed only to "Acme.Utilities", never to
        // "System.Web" (or any other target) even though both harvests originate from the same
        // source file.
        var match = Assert.Single(results, r =>
            r.FilePath.EndsWith("BareLogger.cs", StringComparison.Ordinal));

        Assert.Equal(UsageConfidence.SuffixMatch, match.Confidence);
        Assert.Equal("Acme.Utilities", match.TargetName);
        Assert.Equal("Logger", match.MatchedSymbol);

        Assert.DoesNotContain(results, r =>
            r.FilePath.EndsWith("BareLogger.cs", StringComparison.Ordinal) &&
            r.TargetName != "Acme.Utilities");
    }

    [Fact]
    public async Task ScanAsync_UnrelatedFileProducesNoMatchesFromAnyPass()
    {
        var results = await ScanAsync();

        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("Unrelated.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_HarvestSourceFileItselfHasOnlyTheTwoConfirmedMemberAccesses()
    {
        var results = await ScanAsync();

        // Sanity check on the harvest source itself: exactly the two Confirmed MemberAccess
        // results that seed the harvest -- the harvested suffixes don't otherwise appear anywhere
        // else in that same file beyond the already-confirmed lines, so no SuffixMatch noise
        // leaks in from the source file's own harvest.
        var fromSource = results.Where(r => r.FilePath.EndsWith("Source.cs", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, fromSource.Count);
        Assert.All(fromSource, r => Assert.Equal(UsageConfidence.Confirmed, r.Confidence));
        Assert.Contains(fromSource, r => r.TargetName == "System.Web" && r.MatchedSymbol == "System.Web.HttpContext");
        Assert.Contains(fromSource, r => r.TargetName == "Acme.Utilities" && r.MatchedSymbol == "Acme.Utilities.Logger");
    }
}
