using System.Text.Json;
using SlnParser;
using SlnParser.Contracts;

namespace DepAnalyzer;

public static class SolutionReader
{
    public static IReadOnlyList<string> GetProjectPaths(string inputPath)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        return ext switch
        {
            ".sln" => ReadSln(inputPath),
            ".slnf" => ReadSlnf(inputPath),
            _ => throw new ArgumentException($"Unsupported file type: {ext}. Expected .sln or .slnf.")
        };
    }

    private static IReadOnlyList<string> ReadSln(string slnPath)
    {
        var parser = new SolutionParser();
        var solution = parser.Parse(slnPath);

        // SolutionProject.File is already a resolved absolute FileInfo
        return solution.AllProjects
            .OfType<SolutionProject>()
            .Where(p =>
            {
                var fileExt = p.File.Extension.ToLowerInvariant();
                return fileExt is ".csproj" or ".vbproj";
            })
            .Select(p => p.File.FullName)
            .ToList();
    }

    private static IReadOnlyList<string> ReadSlnf(string slnfPath)
    {
        var json = File.ReadAllText(slnfPath);
        using var doc = JsonDocument.Parse(json);

        var slnfDir = Path.GetDirectoryName(slnfPath)!;
        var solutionRelPath = doc.RootElement
            .GetProperty("solution")
            .GetProperty("path")
            .GetString() ?? throw new InvalidDataException("Missing 'solution.path' in .slnf file.");

        // Project paths in .slnf are relative to the parent .sln's directory
        var slnPath = Path.GetFullPath(
            Path.Combine(slnfDir, solutionRelPath.Replace('\\', Path.DirectorySeparatorChar)));
        var slnDir = Path.GetDirectoryName(slnPath)!;

        return doc.RootElement
            .GetProperty("solution")
            .GetProperty("projects")
            .EnumerateArray()
            .Select(e => e.GetString() ?? "")
            .Where(p => !string.IsNullOrEmpty(p))
            .Where(p =>
            {
                var fileExt = Path.GetExtension(p).ToLowerInvariant();
                return fileExt is ".csproj" or ".vbproj";
            })
            .Select(p =>
            {
                var normalized = p.Replace('\\', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(slnDir, normalized));
            })
            .ToList();
    }
}
