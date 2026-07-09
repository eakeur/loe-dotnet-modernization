using System.Reflection;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.PackageCompatibility;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.Search;
using DotNetModAssess.Core.UsageScanning;
using DotNetModAssess.Core.Workspaces;
using DotNetModAssess.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The solution/filter/project path is a required command-line argument, not a tool parameter and
// not interactive - the engineer configures it once in their MCP client's config (e.g. Claude
// Code's .mcp.json: {"mcpServers": {"dotnetmodassess": {"command": "dotnet", "args": ["run",
// "--project", "src/DotNetModAssess.Mcp", "--", "/path/to/MySolution.sln"]}}}). Fail fast with a
// clear usage message rather than guessing or falling back silently.
if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: DotNetModAssess.Mcp <path-to-.sln|.slnf|.csproj>");
    return 1;
}

var solutionPath = args[0];

var builder = Host.CreateApplicationBuilder(args);

// MCP over stdio uses stdout exclusively for JSON-RPC protocol messages - any other output on
// stdout (including the default console logger) corrupts the protocol stream. Route all logging
// to stderr instead.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Real Buildalyzer-backed parser + graph builder + Roslyn usage scanner + legacy-pattern
// detectors, mirroring exactly how src/DotNetModAssess.Web/Program.cs registers these (see that
// file, including the 5 individual ILegacyPatternDetector registrations LegacyPatternScanner fans
// out to). Everything here is Singleton, not Scoped like the Web layer's per-circuit
// registrations: this process serves exactly one loaded workspace for its whole lifetime by
// design (see McpWorkspaceState's own doc comment), so there is no "per connection" concept to
// scope to.
builder.Services.AddSingleton<ISolutionParser, BuildalyzerSolutionParser>();
builder.Services.AddSingleton<IDependencyGraphBuilder, DependencyGraphBuilder>();
builder.Services.AddSingleton<IUsageScanner, RoslynUsageScanner>();
builder.Services.AddSingleton<IAdHocSourceSearcher, AdHocSourceSearcher>();
builder.Services.AddSingleton<INuGetCompatibilityChecker, NuGetCompatibilityChecker>();
builder.Services.AddSingleton<IRecentWorkspacesStore, JsonFileRecentWorkspacesStore>();

builder.Services.AddSingleton<ILegacyPatternDetector, WcfPatternDetector>();
builder.Services.AddSingleton<ILegacyPatternDetector, WpfPatternDetector>();
builder.Services.AddSingleton<ILegacyPatternDetector, ConfigurationManagerPatternDetector>();
builder.Services.AddSingleton<ILegacyPatternDetector, AppDomainPatternDetector>();
builder.Services.AddSingleton<ILegacyPatternDetector, ComInteropPatternDetector>();
builder.Services.AddSingleton<ILegacyPatternScanner, LegacyPatternScanner>();

builder.Services.AddSingleton<McpWorkspaceState>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

var host = builder.Build();

// Kick off loading as a background Task BEFORE host.RunAsync() starts the MCP protocol loop
// accepting connections - the initialize handshake must never be delayed by a multi-minute
// solution parse, regardless of solution size. Every data tool awaits
// McpWorkspaceState.LoadingTask internally, so a tool invoked before this finishes transparently
// waits rather than needing its own retry logic; get_load_status is the sole exception, returning
// immediately without awaiting so an agent can poll progress instead of blindly waiting on a data
// tool.
var workspaceState = host.Services.GetRequiredService<McpWorkspaceState>();
_ = workspaceState.StartAsync(solutionPath);

await host.RunAsync();

return 0;
