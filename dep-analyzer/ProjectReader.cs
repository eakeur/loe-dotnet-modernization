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
                    var rawLines = File.ReadAllLines(file);
                    var key = ext.TrimStart('.').ToLowerInvariant();
                    var count = key switch
                    {
                        "cs" or "razor" or "cshtml" => CountCStyleLines(rawLines),
                        "vb"                         => CountVbLines(rawLines),
                        "csproj" or "vbproj" or "asmx" or "resx"
                            or "xml" or "config" or "aspx" or "ascx" => CountXmlLines(rawLines),
                        _                            => CountNonBlankLines(rawLines),
                    };
                    byExtension[key] = byExtension.GetValueOrDefault(key) + count;
                    total += count;
                }
                catch { /* skip unreadable files */ }
            }
        }
        catch { /* skip unreadable directories */ }

        return (total, byExtension);
    }

    // Counts non-blank, non-comment lines in C# / Razor / CSHTML files.
    // Handles // single-line comments and /* */ block comments.
    private static int CountCStyleLines(string[] lines)
    {
        var count = 0;
        var inBlock = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (inBlock)
            {
                var end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) continue; // still inside block comment

                inBlock = false;
                // Any non-comment content after */ on the same line?
                var after = line.Substring(end + 2).Trim();
                if (after.Length > 0 && !after.StartsWith("//"))
                    count++;
                continue;
            }

            if (line.StartsWith("//")) continue; // single-line comment

            var blockStart = line.IndexOf("/*", StringComparison.Ordinal);
            if (blockStart >= 0)
            {
                var before = line.Substring(0, blockStart).Trim();
                var blockEnd = line.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
                if (blockEnd < 0)
                {
                    inBlock = true;
                    if (before.Length > 0) count++; // code precedes the opening /*
                }
                else
                {
                    // Inline block comment (opens and closes on same line)
                    var after = line.Substring(blockEnd + 2).Trim();
                    if (before.Length > 0 || (after.Length > 0 && !after.StartsWith("//")))
                        count++;
                }
                continue;
            }

            count++;
        }

        return count;
    }

    // Counts non-blank, non-comment lines in VB.NET files.
    // Single-line comments start with ' or REM.
    private static int CountVbLines(string[] lines)
    {
        var count = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("'")) continue;
            if (line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase)) continue;
            count++;
        }
        return count;
    }

    // Counts non-blank, non-comment lines in XML-based files.
    // Handles <!-- --> block comments.
    private static int CountXmlLines(string[] lines)
    {
        var count = 0;
        var inComment = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (inComment)
            {
                if (line.Contains("-->")) inComment = false;
                continue;
            }

            var commentStart = line.IndexOf("<!--", StringComparison.Ordinal);
            if (commentStart == 0 && !line.Contains("-->"))
            {
                inComment = true;
                continue;
            }

            // Inline <!-- comment --> — the rest of the line still has markup, count it
            if (commentStart > 0 && line.IndexOf("-->", commentStart + 4, StringComparison.Ordinal) < 0)
                inComment = true;

            count++;
        }

        return count;
    }

    private static int CountNonBlankLines(string[] lines) =>
        lines.Count(l => l.Trim().Length > 0);
}
