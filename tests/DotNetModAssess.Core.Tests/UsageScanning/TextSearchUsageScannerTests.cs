using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.UsageScanning;

namespace DotNetModAssess.Core.Tests.UsageScanning;

/// <summary>
/// Exercises the second, text-search usage-detection pass that <see cref="RoslynUsageScanner"/>
/// unions into its results (see that class's own doc comment for the two-pass architecture).
/// Uses a small, dedicated fixture project (under
/// "UsageScanningFixtures/TextSearchProject") rather than the shared fixtures in
/// <see cref="RoslynUsageScannerTests"/>, so these scenarios (bare/string-literal usage, dedup,
/// true-negative) are isolated and easy to reason about independently.
/// </summary>
public class TextSearchUsageScannerTests
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

            return Path.Combine(dir.FullName, "tests", "DotNetModAssess.Core.Tests", "UsageScanningFixtures", "TextSearchProject");
        }
    }

    private static SolutionModel BuildSolution()
    {
        var project = new ProjectModel
        {
            // The .csproj file itself need not exist -- the scanner only walks the containing
            // directory, per the convention documented on RoslynUsageScanner.
            Path = Path.Combine(FixturesRoot, "TextSearchSample.App.csproj"),
            Name = "TextSearchSample.App",
            TargetFrameworks = ["net8.0"],
            Format = ProjectFormat.SdkStyle,
            OutputType = ProjectOutputType.Library,
            PackageReferences = [new PackageReferenceModel { PackageId = "System.Web" }],
            Metadata = new ProjectMetadata
            {
                RawProperties = new Dictionary<string, string?>(),
                Format = ProjectFormat.SdkStyle,
                PackagesModel = PackagesModel.PackageReference,
                TargetFrameworkRaw = "net8.0",
                TargetFrameworkClassification = TargetFrameworkClassification.Modern,
                RootNamespace = "TextSearchSample.App",
                AssemblyName = "TextSearchSample.App",
            },
        };

        return new SolutionModel
        {
            Path = Path.Combine(FixturesRoot, "TextSearchSample.sln"),
            Projects = [project],
        };
    }

    private static Task<IReadOnlyList<UsageResult>> ScanAsync() =>
        new RoslynUsageScanner().ScanAsync(BuildSolution());

    [Fact]
    public async Task ScanAsync_FindsStringLiteralBareUsageThatRoslynAloneWouldMiss()
    {
        var results = await ScanAsync();

        // ReflectionUsage.cs references "System.Web" only inside a string literal passed to
        // Type.GetType -- there is no using directive / type reference / member access syntax
        // node for it, so the Roslyn pass structurally cannot see it. Only the text-search pass
        // can, and it must be tagged TextMatch (not Confirmed).
        var match = Assert.Single(results, r => r.FilePath.EndsWith("ReflectionUsage.cs", StringComparison.Ordinal));

        Assert.Equal(UsageConfidence.TextMatch, match.Confidence);
        Assert.Equal("System.Web", match.MatchedSymbol);
        Assert.Equal(UsageReferenceKind.Other, match.Kind);
        Assert.Equal(12, match.LineNumber);
        Assert.Contains("Type.GetType", match.CodeSnippet);
    }

    [Fact]
    public async Task ScanAsync_FullyQualifiedUsingDirectiveStaysConfirmed()
    {
        var results = await ScanAsync();

        var match = Assert.Single(results, r => r.FilePath.EndsWith("ConfirmedUsage.cs", StringComparison.Ordinal));

        Assert.Equal(UsageConfidence.Confirmed, match.Confidence);
        Assert.Equal(UsageReferenceKind.UsingDirective, match.Kind);
        Assert.Equal("System.Web", match.MatchedSymbol);
        Assert.Equal(1, match.LineNumber);
    }

    [Fact]
    public async Task ScanAsync_DoesNotDoubleReportAnOccurrenceFoundByBothPasses()
    {
        var results = await ScanAsync();

        // The `using System.Web;` line in ConfirmedUsage.cs is found by *both* the Roslyn pass
        // and the text-search pass (the literal text "System.Web" is right there on that line).
        // The text pass must de-duplicate against the Roslyn result by (FilePath, LineNumber,
        // MatchedSymbol), so there must be exactly one result for that occurrence overall -- not
        // one Confirmed and one TextMatch for the same physical line.
        var occurrences = results.Where(r =>
            r.FilePath.EndsWith("ConfirmedUsage.cs", StringComparison.Ordinal) &&
            r.LineNumber == 1 &&
            r.MatchedSymbol == "System.Web").ToList();

        var only = Assert.Single(occurrences);
        Assert.Equal(UsageConfidence.Confirmed, only.Confidence);
    }

    [Fact]
    public async Task ScanAsync_UnrelatedFileProducesNoMatchesFromEitherPass()
    {
        var results = await ScanAsync();

        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("Unrelated.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_ProducesExactlyTheExpectedTwoResultsAcrossTheWholeFixture()
    {
        var results = await ScanAsync();

        // Sanity check on the whole fixture: exactly one Confirmed result (the `using` directive)
        // and exactly one TextMatch result (the reflection string literal) -- nothing else leaks
        // in from either pass.
        Assert.Equal(2, results.Count);
        Assert.Equal(1, results.Count(r => r.Confidence == UsageConfidence.Confirmed));
        Assert.Equal(1, results.Count(r => r.Confidence == UsageConfidence.TextMatch));
    }
}
