using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests.LegacyPatterns;

public class AppDomainPatternDetectorTests
{
    private static Task<IReadOnlyList<UsageResult>> DetectAsync(params ProjectModel[] projects) =>
        new AppDomainPatternDetector().DetectAsync(LegacyPatternsFixtureHelpers.BuildSolution(projects));

    [Fact]
    public async Task DetectAsync_FlagsCreateDomainCurrentDomainAndUnload_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("AppDomainProject", "SampleApp.Domains");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "AppDomain.CreateDomain" &&
            r.Confidence == UsageConfidence.Confirmed &&
            r.TargetName == "AppDomain");

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "AppDomain.CurrentDomain" &&
            r.Confidence == UsageConfidence.Confirmed);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "AppDomain.Unload" &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_FlagsMarshalByRefObjectBaseType_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("AppDomainProject", "SampleApp.Domains");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.BaseTypeOrInterface &&
            r.MatchedSymbol == "MarshalByRefObject" &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_UnrelatedProject_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("NoWpfProject", "SampleApp.NoWpf");
        var results = await DetectAsync(project);

        Assert.Empty(results);
    }

    [Fact]
    public async Task DetectAsync_UnrelatedFileInAppDomainProject_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("AppDomainProject", "SampleApp.Domains");
        var results = await DetectAsync(project);

        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("Unrelated.cs", StringComparison.Ordinal));
    }
}
