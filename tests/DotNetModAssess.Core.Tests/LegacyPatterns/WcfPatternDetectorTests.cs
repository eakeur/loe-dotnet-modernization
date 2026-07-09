using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests.LegacyPatterns;

public class WcfPatternDetectorTests
{
    private static Task<IReadOnlyList<UsageResult>> DetectAsync(params ProjectModel[] projects) =>
        new WcfPatternDetector().DetectAsync(LegacyPatternsFixtureHelpers.BuildSolution(projects));

    [Fact]
    public async Task DetectAsync_FlagsServiceContractAndOperationContractAttributes_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WcfProject", "SampleApp.Wcf");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "ServiceContract" &&
            r.Confidence == UsageConfidence.Confirmed &&
            r.TargetName == "WCF" &&
            r.FilePath.EndsWith("OrderServiceContract.cs", StringComparison.Ordinal));

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "OperationContract" &&
            r.Confidence == UsageConfidence.Confirmed);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "DataContract" &&
            r.Confidence == UsageConfidence.Confirmed);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "DataMember" &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_FlagsServiceHostAndChannelFactoryInstantiations_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WcfProject", "SampleApp.Wcf");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.TypeReference &&
            r.MatchedSymbol == "ServiceHost" &&
            r.Confidence == UsageConfidence.Confirmed);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.TypeReference &&
            r.MatchedSymbol.StartsWith("ChannelFactory", StringComparison.Ordinal) &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_FlagsClientBaseBaseType_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WcfProject", "SampleApp.Wcf");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.BaseTypeOrInterface &&
            r.MatchedSymbol.StartsWith("ClientBase", StringComparison.Ordinal) &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_FlagsBareNamespaceUsing_AsTextMatchOnly()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WcfProject", "SampleApp.Wcf");
        var results = await DetectAsync(project);

        var usingFindings = results.Where(r =>
            r.Kind == UsageReferenceKind.UsingDirective &&
            r.FilePath.EndsWith("BareNamespaceMention.cs", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(usingFindings);
        Assert.All(usingFindings, r => Assert.Equal(UsageConfidence.TextMatch, r.Confidence));
    }

    [Fact]
    public async Task DetectAsync_UnrelatedProject_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("NoWpfProject", "SampleApp.NoWpf");
        var results = await DetectAsync(project);

        Assert.Empty(results);
    }

    [Fact]
    public async Task DetectAsync_UnrelatedFileInWcfProject_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WcfProject", "SampleApp.Wcf");
        var results = await DetectAsync(project);

        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("Unrelated.cs", StringComparison.Ordinal));
    }
}
