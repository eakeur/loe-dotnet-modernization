using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests.LegacyPatterns;

public class WpfPatternDetectorTests
{
    private static Task<IReadOnlyList<UsageResult>> DetectAsync(params ProjectModel[] projects) =>
        new WpfPatternDetector().DetectAsync(LegacyPatternsFixtureHelpers.BuildSolution(projects));

    [Fact]
    public async Task DetectAsync_FlagsXamlFilePresence_AsConfirmed_IndependentOfCSharpParsing()
    {
        // WpfProject/MainWindow.xaml is a real file on disk (checked in as a fixture) - this
        // finding is produced purely from Directory.EnumerateFiles, not from parsing any C#.
        var project = LegacyPatternsFixtureHelpers.CreateProject("WpfProject", "SampleApp.Wpf");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Other &&
            r.MatchedSymbol == "MainWindow.xaml" &&
            r.Confidence == UsageConfidence.Confirmed &&
            r.TargetName == "WPF" &&
            r.FilePath.EndsWith("MainWindow.xaml", StringComparison.Ordinal) &&
            r.LineNumber == 1);
    }

    [Fact]
    public async Task DetectAsync_NoXamlFileInProjectDirectory_ProducesNoXamlPresenceFinding()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("NoWpfProject", "SampleApp.NoWpf");
        var results = await DetectAsync(project);

        Assert.DoesNotContain(results, r => r.Kind == UsageReferenceKind.Other);
    }

    [Fact]
    public async Task DetectAsync_FlagsWindowBaseTypeAndDependencyPropertyRegister_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WpfProject", "SampleApp.Wpf");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.BaseTypeOrInterface &&
            r.MatchedSymbol == "Window" &&
            r.Confidence == UsageConfidence.Confirmed &&
            r.FilePath.EndsWith("MainWindow.xaml.cs", StringComparison.Ordinal));

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.MemberAccess &&
            r.MatchedSymbol == "DependencyProperty.Register" &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_FlagsSystemWindowsUsing_AsConfirmed_WhenUseWpfPropertyIsSet()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WpfProject", "SampleApp.Wpf", useWpf: true);
        var results = await DetectAsync(project);

        var usingFindings = results.Where(r =>
            r.Kind == UsageReferenceKind.UsingDirective &&
            r.MatchedSymbol == "System.Windows").ToList();

        Assert.NotEmpty(usingFindings);
        Assert.All(usingFindings, r => Assert.Equal(UsageConfidence.Confirmed, r.Confidence));
    }

    [Fact]
    public async Task DetectAsync_FlagsSystemWindowsUsing_AsTextMatchOnly_WhenUseWpfPropertyIsNotSet()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WpfProject", "SampleApp.Wpf", useWpf: false);
        var results = await DetectAsync(project);

        var usingFindings = results.Where(r =>
            r.Kind == UsageReferenceKind.UsingDirective &&
            r.MatchedSymbol == "System.Windows").ToList();

        Assert.NotEmpty(usingFindings);
        Assert.All(usingFindings, r => Assert.Equal(UsageConfidence.TextMatch, r.Confidence));
    }

    [Fact]
    public async Task DetectAsync_UnrelatedFileInWpfProject_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("WpfProject", "SampleApp.Wpf");
        var results = await DetectAsync(project);

        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("Unrelated.cs", StringComparison.Ordinal));
    }
}
