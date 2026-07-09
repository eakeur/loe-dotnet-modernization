using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests.LegacyPatterns;

public class ConfigurationManagerPatternDetectorTests
{
    private static Task<IReadOnlyList<UsageResult>> DetectAsync(params ProjectModel[] projects) =>
        new ConfigurationManagerPatternDetector().DetectAsync(LegacyPatternsFixtureHelpers.BuildSolution(projects));

    [Fact]
    public async Task DetectAsync_FlagsBareAccess_AsConfirmed_WhenFileHasCorroboratingUsingDirective()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("ConfigManagerProject", "SampleApp.ConfigManager");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "ConfigurationManager.AppSettings" &&
            r.Confidence == UsageConfidence.Confirmed &&
            r.TargetName == "ConfigurationManager" &&
            r.FilePath.EndsWith("ConfirmedUsage.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DetectAsync_FlagsFullyQualifiedAccess_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("ConfigManagerProject", "SampleApp.ConfigManager");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "System.Configuration.ConfigurationManager.ConnectionStrings" &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_FlagsBareAccess_AsTextMatchOnly_WhenFileHasNoCorroboratingUsingDirective()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("ConfigManagerProject", "SampleApp.ConfigManager");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "ConfigurationManager.AppSettings" &&
            r.Confidence == UsageConfidence.TextMatch &&
            r.FilePath.EndsWith("BareUsage.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DetectAsync_UnrelatedProject_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("NoWpfProject", "SampleApp.NoWpf");
        var results = await DetectAsync(project);

        Assert.Empty(results);
    }
}
