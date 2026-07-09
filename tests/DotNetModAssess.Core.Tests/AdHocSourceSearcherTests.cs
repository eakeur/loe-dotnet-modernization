using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Search;

namespace DotNetModAssess.Core.Tests;

/// <summary>
/// Exercises <see cref="AdHocSourceSearcher"/> against a throwaway temp project directory (same
/// isolation approach as <see cref="EvaluationCacheTests"/>'s <c>CreateTempProject</c>) containing
/// real files on disk, since the searcher reads from the filesystem rather than an in-memory
/// abstraction.
/// </summary>
public class AdHocSourceSearcherTests
{
    private static (SolutionModel Solution, string ProjectDir) CreateTempSolution()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AdHocSourceSearcherTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var projectPath = Path.Combine(dir, "Sample.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var project = new ProjectModel
        {
            Path = projectPath,
            Name = "Sample",
            TargetFrameworks = ["net8.0"],
            Metadata = new ProjectMetadata
            {
                RawProperties = new Dictionary<string, string?>(),
                TargetFrameworkRaw = "net8.0",
            },
        };

        var solution = new SolutionModel
        {
            Path = Path.Combine(dir, "Sample.sln"),
            Projects = [project],
        };

        return (solution, dir);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_ForBlankQuery()
    {
        var (solution, dir) = CreateTempSolution();
        try
        {
            var searcher = new AdHocSourceSearcher();
            var results = await searcher.SearchAsync(solution, "   ");

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_FindsWordBoundaryMatch_AndTagsAsTextMatch()
    {
        var (solution, dir) = CreateTempSolution();
        try
        {
            var filePath = Path.Combine(dir, "Widget.cs");
            await File.WriteAllTextAsync(filePath, "namespace Sample;\n\npublic class WidgetFactory { }\n");

            var searcher = new AdHocSourceSearcher();
            var results = await searcher.SearchAsync(solution, "WidgetFactory");

            var result = Assert.Single(results);
            Assert.Equal(Path.GetFullPath(filePath), Path.GetFullPath(result.FilePath));
            Assert.Equal(3, result.LineNumber);
            Assert.Equal(UsageConfidence.TextMatch, result.Confidence);
            Assert.Equal("WidgetFactory", result.TargetName);
            Assert.Equal("WidgetFactory", result.MatchedSymbol);
            Assert.Equal(UsageReferenceKind.Other, result.Kind);
            Assert.Equal(solution.Projects[0].Path, result.ProjectPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_DoesNotMatch_SubstringWithoutWordBoundary()
    {
        var (solution, dir) = CreateTempSolution();
        try
        {
            var filePath = Path.Combine(dir, "Widget.cs");
            await File.WriteAllTextAsync(filePath, "public class WidgetFactoryHelper { }\n");

            var searcher = new AdHocSourceSearcher();
            var results = await searcher.SearchAsync(solution, "WidgetFactory");

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_ExcludesBinAndObjDirectories()
    {
        var (solution, dir) = CreateTempSolution();
        try
        {
            var binDir = Path.Combine(dir, "bin");
            Directory.CreateDirectory(binDir);
            await File.WriteAllTextAsync(Path.Combine(binDir, "Generated.cs"), "class Needle { }\n");

            var searcher = new AdHocSourceSearcher();
            var results = await searcher.SearchAsync(solution, "Needle");

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_OnlySearchesKnownExtensions()
    {
        var (solution, dir) = CreateTempSolution();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "notes.txt"), "Needle\n");

            var searcher = new AdHocSourceSearcher();
            var results = await searcher.SearchAsync(solution, "Needle");

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
