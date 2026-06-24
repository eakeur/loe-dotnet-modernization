namespace DepTree;

public static class SolutionAnalyzer
{
    public static SolutionOutput Analyze(string solutionPath, TextWriter log)
    {
        var (rootDir, slnFile) = ResolveSolutionRoot(solutionPath);

        log.WriteLine();
        log.WriteLine("  .NET Solution Dependency Tree Builder");
        log.WriteLine("  ======================================");
        log.WriteLine();
        log.WriteLine($"  Root     : {rootDir}");
        if (slnFile is not null)
            log.WriteLine($"  Solution : {slnFile}");
        log.WriteLine();

        // ── 1. Discover all csproj files ────────────────────────────────
        var csprojFiles = Directory
            .GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        if (csprojFiles.Count == 0)
            throw new InvalidOperationException($"No .csproj files found under: {rootDir}");

        log.WriteLine($"  Found {csprojFiles.Count} project(s)");
        log.WriteLine();

        // ── 2. Parse all projects ────────────────────────────────────────
        var projectIndex = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in csprojFiles)
        {
            log.WriteLine($"  Parsing  : {Path.GetFileName(file)}");
            try
            {
                var info = ProjectParser.Parse(file, rootDir);
                projectIndex[file] = info;
            }
            catch (Exception ex)
            {
                log.WriteLine($"  WARNING  : Could not parse {file}: {ex.Message}");
            }
        }

        // ── 3. Resolve cross-project references ──────────────────────────
        log.WriteLine();
        log.WriteLine("  Resolving cross-project references...");

        foreach (var proj in projectIndex.Values)
        {
            var resolvedRefs = new List<string>();

            foreach (var refPath in proj.ProjectRefs)
            {
                if (projectIndex.TryGetValue(refPath, out var target))
                {
                    resolvedRefs.Add(target.Name);
                    // Add reverse (dependent) edge
                    target.Dependents.Add(proj.Name);
                }
                else
                {
                    resolvedRefs.Add(Path.GetFileNameWithoutExtension(refPath) + " (external)");
                }
            }

            proj.ProjectRefs = resolvedRefs;
        }

        // ── 4. Build summary ─────────────────────────────────────────────
        var allProjects = projectIndex.Values.ToList();

        var topPackages = allProjects
            .SelectMany(p => p.Packages)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(30)
            .Select(g => new TopPackage
            {
                Name     = g.Key,
                UsedBy   = g.Count(),
                Versions = [.. g.Select(p => p.Version).Distinct().Order()],
            })
            .ToList();

        var frameworkSummary = allProjects
            .GroupBy(p => p.FrameworkClass)
            .Select(g => new FrameworkGroup
            {
                Class = g.Key,
                Count = g.Count(),
                Tfms  = [.. g.SelectMany(p => p.TargetFrameworks).Distinct().Order()],
            })
            .ToList();

        var summary = new SolutionSummary
        {
            GeneratedAt      = DateTime.UtcNow.ToString("o"),
            SolutionPath     = slnFile ?? rootDir,
            TotalProjects    = allProjects.Count,
            TestProjects     = allProjects.Count(p => p.IsTestProject),
            RiskSummary      = new RiskCount
            {
                High   = allProjects.Count(p => p.MigrationRisk.Level == "High"),
                Medium = allProjects.Count(p => p.MigrationRisk.Level == "Medium"),
                Low    = allProjects.Count(p => p.MigrationRisk.Level == "Low"),
            },
            FrameworkSummary = frameworkSummary,
            TopPackages      = topPackages,
        };

        // ── 5. Print summary ─────────────────────────────────────────────
        log.WriteLine();
        log.WriteLine("  -- Summary ------------------------------------------");
        log.WriteLine($"  Total projects : {summary.TotalProjects}");
        log.WriteLine($"  Test projects  : {summary.TestProjects}");
        log.WriteLine($"  Risk - High    : {summary.RiskSummary.High}");
        log.WriteLine($"  Risk - Medium  : {summary.RiskSummary.Medium}");
        log.WriteLine($"  Risk - Low     : {summary.RiskSummary.Low}");
        log.WriteLine();
        log.WriteLine("  Framework breakdown:");
        foreach (var f in frameworkSummary)
            log.WriteLine($"    {f.Class,-22} {f.Count} project(s)  [{string.Join(", ", f.Tfms)}]");

        return new SolutionOutput
        {
            Summary  = summary,
            Projects = allProjects,
        };
    }

    private static (string RootDir, string? SlnFile) ResolveSolutionRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath) && fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return (Path.GetDirectoryName(fullPath)!, fullPath);

        if (Directory.Exists(fullPath))
        {
            var slnFiles = Directory.GetFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly);
            return (fullPath, slnFiles.Length > 0 ? slnFiles[0] : null);
        }

        throw new ArgumentException($"Path not found: {path}");
    }
}
