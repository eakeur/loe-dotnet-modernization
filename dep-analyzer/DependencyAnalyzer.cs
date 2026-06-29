using System.Xml.Linq;

namespace DepAnalyzer;

public class DependencyAnalyzer
{
    public List<DependencyRow> Analyze(IReadOnlyList<ProjectInfo> projects, string? solutionDir = null)
    {
        var solutionSet = projects.Select(p => p.AbsolutePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var levels = ComputeProjectLevels(projects, solutionSet);
        var cpmVersions = LoadCpmVersions(solutionDir);

        if (cpmVersions.Count > 0)
            Console.Error.WriteLine($"Central Package Management detected — loaded {cpmVersions.Count} versions from Directory.Packages.props.");

        var rows = new List<DependencyRow>();

        // One row per project
        foreach (var project in projects)
        {
            var internalDeps = project.ProjectRefPaths
                .Count(r => solutionSet.Contains(r));

            var loc = project.LineCountsByExtension;
            rows.Add(new DependencyRow
            {
                Name = project.Name,
                Type = "ProjectReference",
                Version = "",
                TargetFramework = project.TargetFramework,
                SupportsNet8 = null,
                InternalProjectDependencies = internalDeps,
                IsTestProject = project.IsTestProject,
                ProjectFormat = project.IsSdkStyle ? "SDK-Style" : "Legacy",
                Level = levels.TryGetValue(project.AbsolutePath, out var lvl) ? lvl : -1,
                LinesOfCode = project.TotalLinesOfCode,
                LinesOfCode_cs = loc.GetValueOrDefault("cs"),
                LinesOfCode_vb = loc.GetValueOrDefault("vb"),
                LinesOfCode_csproj = loc.GetValueOrDefault("csproj"),
                LinesOfCode_vbproj = loc.GetValueOrDefault("vbproj"),
                LinesOfCode_asmx = loc.GetValueOrDefault("asmx"),
                LinesOfCode_resx = loc.GetValueOrDefault("resx"),
                LinesOfCode_json = loc.GetValueOrDefault("json"),
                LinesOfCode_xml = loc.GetValueOrDefault("xml"),
                LinesOfCode_config = loc.GetValueOrDefault("config"),
                LinesOfCode_aspx = loc.GetValueOrDefault("aspx"),
                LinesOfCode_ascx = loc.GetValueOrDefault("ascx"),
                LinesOfCode_razor = loc.GetValueOrDefault("razor"),
                LinesOfCode_cshtml = loc.GetValueOrDefault("cshtml"),
            });
        }

        // One row per unique NuGet package (deduplicated across all projects, case-insensitive id)
        var allPackages = new Dictionary<string, PackageRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            foreach (var pkg in project.Packages)
            {
                if (!string.IsNullOrEmpty(pkg.Id) && !allPackages.ContainsKey(pkg.Id))
                    allPackages[pkg.Id] = pkg;
            }
        }

        foreach (var pkg in allPackages.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            // Fill in version from CPM if missing
            var version = pkg.Version;
            if (string.IsNullOrEmpty(version) && cpmVersions.TryGetValue(pkg.Id, out var cpmVer))
                version = cpmVer;

            rows.Add(new DependencyRow
            {
                Name = pkg.Id,
                Type = "NuGetPackage",
                Version = version,
                TargetFramework = "",
                SupportsNet8 = null,
                InternalProjectDependencies = 0,
                IsTestProject = false,
                ProjectFormat = "N/A",
                Level = 0
            });
        }

        return rows;
    }

    private static Dictionary<string, string> LoadCpmVersions(string? startDir)
    {
        var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(startDir)) return empty;

        var dir = startDir;
        while (dir != null)
        {
            var propsFile = Path.Combine(dir, "Directory.Packages.props");
            if (File.Exists(propsFile))
            {
                try
                {
                    var doc = XDocument.Load(propsFile);
                    return doc.Descendants("PackageVersion")
                        .Select(el => new
                        {
                            Id = el.Attribute("Include")?.Value?.Trim() ?? "",
                            Version = el.Attribute("Version")?.Value?.Trim()
                                      ?? el.Element("Version")?.Value?.Trim()
                                      ?? ""
                        })
                        .Where(x => !string.IsNullOrEmpty(x.Id))
                        .ToDictionary(x => x.Id, x => x.Version, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to read {propsFile}: {ex.Message}");
                    return empty;
                }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return empty;
    }

    private static Dictionary<string, int> ComputeProjectLevels(
        IReadOnlyList<ProjectInfo> projects,
        HashSet<string> solutionSet)
    {
        // Forward adjacency: in-solution project refs only
        var deps = projects.ToDictionary(
            p => p.AbsolutePath,
            p => p.ProjectRefPaths
                   .Where(r => solutionSet.Contains(r))
                   .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        // Reverse adjacency: who depends on me?
        var rdeps = projects.ToDictionary(
            p => p.AbsolutePath,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (path, projectDeps) in deps)
        {
            foreach (var dep in projectDeps)
            {
                if (rdeps.TryGetValue(dep, out var dependents))
                    dependents.Add(path);
            }
        }

        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        // Seed: projects with no in-solution dependencies → Level 0
        foreach (var p in projects)
        {
            if (deps[p.AbsolutePath].Count == 0)
            {
                levels[p.AbsolutePath] = 0;
                queue.Enqueue(p.AbsolutePath);
            }
        }

        // BFS
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var dependent in rdeps[current])
            {
                if (levels.ContainsKey(dependent))
                    continue;

                // Only assign if all of dependent's deps are resolved
                if (deps[dependent].All(d => levels.ContainsKey(d)))
                {
                    levels[dependent] = 1 + deps[dependent].Max(d => levels[d]);
                    queue.Enqueue(dependent);
                }
            }
        }

        // Warn about unresolved (cycles)
        foreach (var p in projects)
        {
            if (!levels.ContainsKey(p.AbsolutePath))
            {
                Console.Error.WriteLine($"Warning: Could not compute level for {p.Name} (possible cycle). Assigning -1.");
                levels[p.AbsolutePath] = -1;
            }
        }

        return levels;
    }
}
