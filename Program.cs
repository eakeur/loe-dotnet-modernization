using System.Text.Json;
using DepTree;

// ── Argument parsing ──────────────────────────────────────────────────────────
var solutionPath = ".";
var outputDir    = ".";
var outputName   = "dependency-tree";
var skipNuGet    = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--solution" or "-s" when i + 1 < args.Length:
            solutionPath = args[++i];
            break;
        case "--output-dir" or "-o" when i + 1 < args.Length:
            outputDir = args[++i];
            break;
        case "--name" or "-n" when i + 1 < args.Length:
            outputName = args[++i];
            break;
        case "--no-nuget" or "--skip-nuget":
            skipNuGet = true;
            break;
        case "--help" or "-h":
            PrintHelp();
            return 0;
    }
}

// ── Run ───────────────────────────────────────────────────────────────────────
try
{
    var data = await SolutionAnalyzer.AnalyzeAsync(solutionPath, Console.Out, skipNuGet);

    Directory.CreateDirectory(outputDir);

    // Write JSON
    var jsonPath = Path.Combine(outputDir, $"{outputName}.json");
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        WriteIndented          = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(data, jsonOptions));
    Console.WriteLine($"\n  JSON     : {jsonPath}");

    // Write HTML
    var htmlPath = Path.Combine(outputDir, $"{outputName}.html");
    await File.WriteAllTextAsync(htmlPath, HtmlReportGenerator.Generate(data));
    Console.WriteLine($"  HTML     : {htmlPath}");

    Console.WriteLine($"\n  Done! Open {outputName}.html in a browser to explore.\n");
    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"\n  ERROR: {ex.Message}\n");
    Console.ResetColor();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""

      dep-tree — .NET Solution Dependency Tree Builder
      =================================================

      Usage:
        dotnet run -- [options]

      Options:
        -s, --solution <path>     Path to .sln file or solution root directory (default: .)
        -o, --output-dir <path>   Output directory for JSON and HTML files (default: .)
        -n, --name <name>         Base name for output files (default: dependency-tree)
            --no-nuget            Skip NuGet compatibility check (faster, offline-friendly)
        -h, --help                Show this help

      Examples:
        dotnet run -- --solution C:\repos\MyApp\MyApp.sln
        dotnet run -- -s C:\repos\MyApp -o C:\reports -n my-solution
        dotnet run -- -s . --no-nuget

    """);
}
