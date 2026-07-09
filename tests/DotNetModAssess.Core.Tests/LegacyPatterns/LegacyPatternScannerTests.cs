using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests.LegacyPatterns;

/// <summary>
/// Exercises the real (frozen, not owned by this module) <see cref="LegacyPatternScanner"/>
/// fanning out across all five real detectors together against a solution containing every
/// detector's own fixture project - the same shape of call <c>Findings.razor</c> makes via
/// <c>ILegacyPatternScanner</c> - to make sure nothing throws and every pattern surfaces findings
/// when its own fixture is present.
/// </summary>
public class LegacyPatternScannerTests
{
    [Fact]
    public async Task ScanAsync_AcrossAllFiveRealDetectors_ProducesFindingsForEveryPattern()
    {
        var solution = LegacyPatternsFixtureHelpers.BuildSolution(
            LegacyPatternsFixtureHelpers.CreateProject("WcfProject", "SampleApp.Wcf"),
            LegacyPatternsFixtureHelpers.CreateProject("WpfProject", "SampleApp.Wpf", useWpf: true),
            LegacyPatternsFixtureHelpers.CreateProject("ConfigManagerProject", "SampleApp.ConfigManager"),
            LegacyPatternsFixtureHelpers.CreateProject("AppDomainProject", "SampleApp.Domains"),
            LegacyPatternsFixtureHelpers.CreateProject(
                "ComInteropProject", "SampleApp.Com", comReferences: ["Microsoft Excel 16.0 Object Library"]));

        ILegacyPatternScanner scanner = new LegacyPatternScanner(
        [
            new WcfPatternDetector(),
            new WpfPatternDetector(),
            new ConfigurationManagerPatternDetector(),
            new AppDomainPatternDetector(),
            new ComInteropPatternDetector(),
        ]);

        var results = await scanner.ScanAsync(solution);

        var patternNames = results.Select(r => r.TargetName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(
            new[] { "AppDomain", "COM Interop", "ConfigurationManager", "WCF", "WPF" },
            patternNames);

        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.FilePath)));
        Assert.All(results, r => Assert.True(r.LineNumber >= 1));
    }
}
