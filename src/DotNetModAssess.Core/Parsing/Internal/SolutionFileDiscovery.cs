using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Build.Construction;

namespace DotNetModAssess.Core.Parsing.Internal;

internal sealed record DiscoveredProject(string Path, string ProjectGuid, string? ProjectTypeGuidRaw);

internal sealed record DiscoveredSolution(string SolutionPath, IReadOnlyList<DiscoveredProject> Projects);

/// <summary>
/// Discovers the set of projects contained in a .sln or .slnf file.
///
/// For .sln we use Microsoft.Build.Construction.SolutionFile (resolved at runtime via
/// Microsoft.Build.Locator against the installed SDK) to get the authoritative project list,
/// and supplement it with a lightweight regex scan of the raw .sln text to recover the
/// per-project "project type" GUID from the `Project("{TYPE-GUID}") = "Name", "path", "{GUID}"`
/// header line, since Microsoft.Build.Construction.ProjectInSolution does not expose it publicly.
///
/// For .slnf (solution filter) we parse the JSON filter, resolve its "solution" path relative to
/// the .slnf's own directory (matching Visual Studio's behavior), parse that parent .sln the same
/// way, and then filter down to just the projects listed in the filter's "projects" array (which
/// are themselves paths relative to the .slnf file).
/// </summary>
internal static class SolutionFileDiscovery
{
    private static readonly Regex ProjectHeaderRegex = new(
        """^Project\("\{(?<typeGuid>[0-9A-Fa-f\-]+)\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]*)"\s*,\s*"\{(?<guid>[0-9A-Fa-f\-]+)\}"\s*$""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static DiscoveredSolution Discover(string solutionOrProjectPath)
    {
        var extension = Path.GetExtension(solutionOrProjectPath);

        if (string.Equals(extension, ".slnf", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoverFromFilter(solutionOrProjectPath);
        }

        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoverFromSolution(solutionOrProjectPath);
        }

        if (IsProjectFileExtension(extension))
        {
            return DiscoverFromStandaloneProject(solutionOrProjectPath);
        }

        throw new NotSupportedException(
            $"Unsupported file type '{extension}' for solution/project loading. Expected a .sln, .slnf, or a project file (.csproj/.vbproj/.fsproj).");
    }

    private static bool IsProjectFileExtension(string extension) =>
        extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A standalone project file (no enclosing .sln at all) is treated as a "solution" containing
    /// just that one project. <see cref="SolutionGraphBuilder"/>'s own recursive ProjectReference
    /// resolution (see its class doc comment) already discovers and builds any referenced projects
    /// on demand even though they're not part of this "discovered" list, so a multi-project
    /// dependency graph still resolves correctly starting from a single entry-point project file.
    /// There's no real .sln to read a project GUID/type GUID from, so both are left empty/null -
    /// downstream metadata mapping already tolerates a missing type GUID (it only adds one when it
    /// actually found one).
    /// </summary>
    private static DiscoveredSolution DiscoverFromStandaloneProject(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var project = new DiscoveredProject(fullPath, ProjectGuid: string.Empty, ProjectTypeGuidRaw: null);
        return new DiscoveredSolution(fullPath, [project]);
    }

    private static DiscoveredSolution DiscoverFromSolution(string solutionPath)
    {
        // SolutionFile.Parse touches Microsoft.Build.Construction, which needs MSBuildLocator
        // registered first (see MSBuildEnvironmentInitializer for why).
        MSBuildEnvironmentInitializer.EnsureRegistered();

        var fullPath = Path.GetFullPath(solutionPath);
        var solutionFile = SolutionFile.Parse(fullPath);
        var typeGuidsByProjectGuid = ParseProjectTypeGuids(fullPath);

        var projects = solutionFile.ProjectsInOrder
            .Where(p => p.ProjectType != SolutionProjectType.SolutionFolder)
            .Select(p => new DiscoveredProject(
                Path.GetFullPath(p.AbsolutePath),
                p.ProjectGuid,
                typeGuidsByProjectGuid.GetValueOrDefault(NormalizeGuidKey(p.ProjectGuid))))
            .ToList();

        return new DiscoveredSolution(fullPath, projects);
    }

    private static DiscoveredSolution DiscoverFromFilter(string filterPath)
    {
        var fullFilterPath = Path.GetFullPath(filterPath);
        var filterDirectory = Path.GetDirectoryName(fullFilterPath)!;

        using var stream = File.OpenRead(fullFilterPath);
        using var document = JsonDocument.Parse(stream);
        var solutionElement = document.RootElement.GetProperty("solution");
        var relativeSolutionPath = solutionElement.GetProperty("path").GetString()
            ?? throw new InvalidOperationException($"Solution filter '{filterPath}' does not specify a solution path.");

        var solutionPath = Path.GetFullPath(Path.Combine(filterDirectory, NormalizeSeparators(relativeSolutionPath)));
        var fullSolution = DiscoverFromSolution(solutionPath);

        if (!solutionElement.TryGetProperty("projects", out var projectsElement) || projectsElement.ValueKind != JsonValueKind.Array)
        {
            // No filter list present - treat as "include everything" per solution filter semantics.
            return fullSolution;
        }

        // Per the .slnf format spec, only "solution.path" is relative to the filter file's own
        // directory (resolved above); every entry in "solution.projects" is relative to the
        // SOLUTION's directory instead - it's meant to match the project paths as they appear
        // inside the .sln itself, regardless of where the .slnf physically lives. Resolving these
        // relative to filterDirectory only happens to work when the .slnf sits right next to the
        // .sln, which is why this was easy to get wrong and not notice.
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var includedPaths = projectsElement
            .EnumerateArray()
            .Select(e => e.GetString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFullPath(Path.Combine(solutionDirectory, NormalizeSeparators(v!))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filteredProjects = fullSolution.Projects
            .Where(p => includedPaths.Contains(p.Path))
            .ToList();

        return new DiscoveredSolution(solutionPath, filteredProjects);
    }

    private static Dictionary<string, string> ParseProjectTypeGuids(string solutionPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = File.ReadAllText(solutionPath);
        foreach (Match match in ProjectHeaderRegex.Matches(text))
        {
            result[NormalizeGuidKey(match.Groups["guid"].Value)] = match.Groups["typeGuid"].Value.ToUpperInvariant();
        }

        return result;
    }

    /// <summary>Strips braces/whitespace and upper-cases a GUID string so lookups are stable
    /// regardless of whether the source (SolutionFile API vs. raw regex scan) included braces.</summary>
    private static string NormalizeGuidKey(string guid) => guid.Trim().Trim('{', '}').ToUpperInvariant();

    private static string NormalizeSeparators(string path) => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
}
