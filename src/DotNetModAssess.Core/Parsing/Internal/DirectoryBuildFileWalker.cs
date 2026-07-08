using System.Xml.Linq;

namespace DotNetModAssess.Core.Parsing.Internal;

/// <summary>
/// Walks the directory tree from a project (or the solution) up towards the filesystem root,
/// discovering Directory.Build.props/.targets and Directory.Packages.props files following
/// MSBuild's directory-walk-up convention.
///
/// Ordering note: results are returned closest-ancestor-first / project-directory-last (i.e.
/// "closest-last"), mirroring MSBuild property-override semantics where a file closer to the
/// project is imported later and can override values set further up the tree. We walk up to
/// (and including) the solution root directory rather than continuing to the filesystem root,
/// since anything above the solution root is outside the scope of a single assessed solution.
/// </summary>
internal static class DirectoryBuildFileWalker
{
    public static IReadOnlyList<string> FindDirectoryBuildFiles(string projectDirectory, string solutionRootDirectory, string fileName)
    {
        var found = new List<string>();
        var current = new DirectoryInfo(projectDirectory);
        var root = new DirectoryInfo(solutionRootDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                found.Add(candidate);
            }

            if (string.Equals(current.FullName, root.FullName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current.Parent;
        }

        found.Reverse(); // closest-last
        return found;
    }

    /// <summary>
    /// Finds the single nearest Directory.Packages.props by walking up from the project
    /// directory, matching NuGet's own central-package-management discovery convention
    /// (unlike Directory.Build.props, only the nearest file applies).
    /// </summary>
    public static string? FindNearestDirectoryPackagesProps(string projectDirectory, string solutionRootDirectory)
    {
        var current = new DirectoryInfo(projectDirectory);
        var root = new DirectoryInfo(solutionRootDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Directory.Packages.props");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (string.Equals(current.FullName, root.FullName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current.Parent;
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> ParsePackageVersions(string directoryPackagesPropsPath)
    {
        if (!File.Exists(directoryPackagesPropsPath))
        {
            return new Dictionary<string, string>();
        }

        var doc = XDocument.Load(directoryPackagesPropsPath);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in doc.Descendants("PackageVersion"))
        {
            var id = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
            var version = element.Attribute("Version")?.Value ?? element.Element(element.Name.Namespace + "Version")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
            {
                result[id] = version;
            }
        }

        return result;
    }

    /// <summary>
    /// Checks whether ManagePackageVersionsCentrally=true appears anywhere in the given
    /// Directory.Build.props chain (used as a fallback when MSBuild evaluation did not
    /// succeed and we cannot read the evaluated property directly).
    /// </summary>
    public static bool ChainEnablesCentralPackageManagement(IEnumerable<string> directoryBuildPropsChain)
    {
        foreach (var path in directoryBuildPropsChain)
        {
            if (!File.Exists(path)) continue;
            var doc = XDocument.Load(path);
            var value = doc.Descendants("ManagePackageVersionsCentrally").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var enabled) && enabled)
            {
                return true;
            }
        }

        return false;
    }
}
