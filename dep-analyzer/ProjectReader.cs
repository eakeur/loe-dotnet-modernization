using System.Xml.Linq;

namespace DepAnalyzer;

public static class ProjectReader
{
    private static readonly HashSet<string> TestPackageKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "xunit", "nunit", "mstest", "Microsoft.NET.Test.Sdk"
    };

    private static readonly HashSet<string> TrackedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".csproj", ".vbproj", ".asmx", ".resx",
        ".json", ".xml", ".config", ".aspx", ".ascx", ".razor", ".cshtml"
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj"
    };

    public static ProjectInfo? Read(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            Console.Error.WriteLine($"Warning: Project file not found, skipping: {csprojPath}");
            return null;
        }

        try
        {
            var doc = XDocument.Load(csprojPath);
            var root = doc.Root!;
            var projectDir = Path.GetDirectoryName(csprojPath)!;
            var name = Path.GetFileNameWithoutExtension(csprojPath);

            var isSdkStyle = root.Attribute("Sdk") != null
                             || root.Elements("Sdk").Any();

            var targetFramework = ReadTargetFramework(root, isSdkStyle);
            var packages = ReadPackages(doc, csprojPath, projectDir, isSdkStyle);
            var projectRefs = ReadProjectRefs(doc, projectDir);
            var isTestProject = DetectTestProject(doc, packages);
            var (totalLoc, locByExt) = CountLines(projectDir);

            return new ProjectInfo(name, csprojPath, isSdkStyle, isTestProject, targetFramework, packages, projectRefs, totalLoc, locByExt);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse {csprojPath}: {ex.Message}");
            return null;
        }
    }

    private static string ReadTargetFramework(XElement root, bool isSdkStyle)
    {
        // SDK-style: <TargetFramework> or <TargetFrameworks>
        var tf = root.Descendants("TargetFramework").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrEmpty(tf)) return tf;

        var tfs = root.Descendants("TargetFrameworks").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrEmpty(tfs)) return tfs;

        // Legacy: <TargetFrameworkVersion> e.g. "v4.8"
        var tfv = root.Descendants("TargetFrameworkVersion").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrEmpty(tfv))
        {
            // Normalize "v4.8" → "net48", "v4.5.2" → "net452"
            var normalized = tfv.TrimStart('v').Replace(".", "");
            return $"net{normalized}";
        }

        return "";
    }

    private static List<PackageRef> ReadPackages(XDocument doc, string csprojPath, string projectDir, bool isSdkStyle)
    {
        // Read PackageReference elements (SDK-style, but can also appear in legacy)
        var pkgRefs = doc.Descendants("PackageReference")
            .Select(el => new PackageRef(
                el.Attribute("Include")?.Value?.Trim() ?? "",
                el.Attribute("Version")?.Value?.Trim()
                    ?? el.Element("Version")?.Value?.Trim()
                    ?? ""))
            .Where(p => !string.IsNullOrEmpty(p.Id))
            .ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);

        // Read packages.config for legacy projects
        var pkgConfigPath = Path.Combine(projectDir, "packages.config");
        if (File.Exists(pkgConfigPath))
        {
            try
            {
                var pkgDoc = XDocument.Load(pkgConfigPath);
                foreach (var el in pkgDoc.Descendants("package"))
                {
                    var id = el.Attribute("id")?.Value?.Trim() ?? "";
                    var version = el.Attribute("version")?.Value?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(id) && !pkgRefs.ContainsKey(id))
                        pkgRefs[id] = new PackageRef(id, version);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to parse packages.config for {csprojPath}: {ex.Message}");
            }
        }

        return pkgRefs.Values.ToList();
    }

    private static List<string> ReadProjectRefs(XDocument doc, string projectDir)
    {
        return doc.Descendants("ProjectReference")
            .Select(el => el.Attribute("Include")?.Value?.Trim() ?? "")
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(rel =>
            {
                var normalized = rel.Replace('\\', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(projectDir, normalized));
            })
            .ToList();
    }

    private static bool DetectTestProject(XDocument doc, List<PackageRef> packages)
    {
        // Explicit property
        if (doc.Descendants("IsTestProject")
               .Any(el => el.Value.Equals("true", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Test framework package references
        return packages.Any(p =>
            p.Id.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
            TestPackageKeywords.Any(kw =>
                kw.Length < p.Id.Length
                    ? p.Id.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    : p.Id.Equals(kw, StringComparison.OrdinalIgnoreCase)));
    }

    private static (int total, Dictionary<string, int> byExtension) CountLines(string projectDir)
    {
        var byExtension = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;

        try
        {
            var files = Directory.EnumerateFiles(projectDir, "*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var relative = f.Substring(projectDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !relative
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(part => SkippedDirectories.Contains(part));
                });

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (!TrackedExtensions.Contains(ext)) continue;

                try
                {
                    var lines = File.ReadAllLines(file).Length;
                    var key = ext.TrimStart('.').ToLowerInvariant();
                    byExtension[key] = byExtension.GetValueOrDefault(key) + lines;
                    total += lines;
                }
                catch { /* skip unreadable files */ }
            }
        }
        catch { /* skip unreadable directories */ }

        return (total, byExtension);
    }
}
