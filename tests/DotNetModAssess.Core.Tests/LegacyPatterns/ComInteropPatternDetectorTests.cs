using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Tests.LegacyPatterns;

public class ComInteropPatternDetectorTests
{
    private static Task<IReadOnlyList<UsageResult>> DetectAsync(params ProjectModel[] projects) =>
        new ComInteropPatternDetector().DetectAsync(LegacyPatternsFixtureHelpers.BuildSolution(projects));

    [Fact]
    public async Task DetectAsync_FlagsComVisibleGuidInterfaceTypeAndDllImportAttributes_AsConfirmed()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("ComInteropProject", "SampleApp.Com");
        var results = await DetectAsync(project);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "ComVisible" &&
            r.Confidence == UsageConfidence.Confirmed &&
            r.TargetName == "COM Interop");

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "Guid" &&
            r.Confidence == UsageConfidence.Confirmed);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "InterfaceType" &&
            r.Confidence == UsageConfidence.Confirmed);

        Assert.Contains(results, r =>
            r.Kind == UsageReferenceKind.Attribute &&
            r.MatchedSymbol == "DllImport" &&
            r.Confidence == UsageConfidence.Confirmed);
    }

    [Fact]
    public async Task DetectAsync_CrossReferencesComReferencesFromProjectMetadata_AtProjectLevel()
    {
        // ComReferences is metadata already parsed out of the raw .csproj upstream - this
        // detector does no file I/O for this cross-reference at all, it just reads
        // ProjectModel.Metadata.LegacySignals.ComReferences directly.
        var project = LegacyPatternsFixtureHelpers.CreateProject(
            "NoWpfProject", "SampleApp.WithComRef", comReferences: ["Microsoft Excel 16.0 Object Library"]);

        var results = await DetectAsync(project);

        var comRefFinding = Assert.Single(results, r => r.Kind == UsageReferenceKind.Other);
        Assert.Equal("Microsoft Excel 16.0 Object Library", comRefFinding.MatchedSymbol);
        Assert.Equal(UsageConfidence.Confirmed, comRefFinding.Confidence);
        Assert.Equal("COM Interop", comRefFinding.TargetName);
        Assert.Equal(project.Path, comRefFinding.FilePath);
        Assert.Equal(1, comRefFinding.LineNumber);
    }

    [Fact]
    public async Task DetectAsync_UnrelatedProjectWithNoComReferences_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("NoWpfProject", "SampleApp.NoWpf");
        var results = await DetectAsync(project);

        Assert.Empty(results);
    }

    [Fact]
    public async Task DetectAsync_UnrelatedFileInComInteropProject_ProducesNoFindings()
    {
        var project = LegacyPatternsFixtureHelpers.CreateProject("ComInteropProject", "SampleApp.Com");
        var results = await DetectAsync(project);

        Assert.DoesNotContain(results, r => r.FilePath.EndsWith("Unrelated.cs", StringComparison.Ordinal));
    }
}
