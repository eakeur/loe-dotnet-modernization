using DepAnalyzer;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    Environment.Exit(args.Length == 0 ? 1 : 0);
}

var solutionPath = args.FirstOrDefault(a => !a.StartsWith("--"));
if (string.IsNullOrEmpty(solutionPath))
{
    Console.Error.WriteLine("Error: Solution path is required.");
    PrintUsage();
    Environment.Exit(1);
}

if (!File.Exists(solutionPath))
{
    Console.Error.WriteLine($"Error: File not found: {solutionPath}");
    Environment.Exit(2);
}

var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
if (ext is not ".sln" and not ".slnf")
{
    Console.Error.WriteLine($"Error: Unsupported file type '{ext}'. Expected .sln or .slnf.");
    Environment.Exit(3);
}

var checkNuget = args.Contains("--check-nuget");

string? outputPath = null;
var outIdx = Array.IndexOf(args, "--output");
if (outIdx >= 0 && outIdx + 1 < args.Length)
    outputPath = args[outIdx + 1];

string? nugetFeed = null;
var feedIdx = Array.IndexOf(args, "--nuget-feed");
if (feedIdx >= 0 && feedIdx + 1 < args.Length)
    nugetFeed = args[feedIdx + 1];

if (string.IsNullOrEmpty(outputPath))
{
    var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
    outputPath = Path.Combine(Directory.GetCurrentDirectory(), $"{solutionName}-deps.csv");
}

try
{
    Console.Error.WriteLine($"Analyzing: {solutionPath}");

    // 1. Discover project paths
    var projectPaths = SolutionReader.GetProjectPaths(solutionPath);
    Console.Error.WriteLine($"Found {projectPaths.Count} project(s).");

    // 2. Parse each project
    var projects = projectPaths
        .Select(ProjectReader.Read)
        .Where(p => p is not null)
        .Select(p => p!)
        .ToList();

    Console.Error.WriteLine($"Parsed {projects.Count} project(s) successfully.");

    // 3. Build dependency graph and compute levels (pass solution dir for CPM support)
    var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath));
    var analyzer = new DependencyAnalyzer();
    var rows = analyzer.Analyze(projects, solutionDir);

    // 4. Optional NuGet compatibility check (auto-discovers sources from nuget.config)
    if (checkNuget)
    {
        var checker = new NuGetCompatibilityChecker(solutionDir, nugetFeed);
        await checker.EnrichAsync(rows, CancellationToken.None);
    }

    // 5. Write CSV
    CsvExporter.Write(rows, outputPath);
    Console.Error.WriteLine($"Done. Output: {outputPath}");
    Console.Error.WriteLine($"Total rows: {rows.Count} ({rows.Count(r => r.Type == "ProjectReference")} projects, {rows.Count(r => r.Type == "NuGetPackage")} packages)");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Environment.Exit(99);
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        dep-analyzer — .NET Solution Dependency Analyzer

        Usage:
          dotnet run -- <solution> [options]

        Arguments:
          <solution>             Path to a .sln or .slnf file

        Options:
          --output <file.csv>    Output CSV path (default: <solutionname>-deps.csv)
          --check-nuget          Query NuGet API to check .NET 8 compatibility
          --nuget-feed <url>     Custom NuGet feed URL (default: https://api.nuget.org/v3/index.json)
          --help, -h             Show this help

        CSV columns:
          Name, Type, Version, TargetFramework, SupportsNet8,
          InternalProjectDependencies, IsTestProject, ProjectFormat, Level
        """);
}
