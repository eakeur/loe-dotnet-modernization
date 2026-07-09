using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.PackageCompatibility;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.Reporting;
using DotNetModAssess.Core.Search;
using DotNetModAssess.Core.UsageScanning;
using DotNetModAssess.Core.Workspaces;
using DotNetModAssess.Web.Components;
using DotNetModAssess.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Real Buildalyzer-backed parser + graph builder + Roslyn usage scanner + report/graph exporters,
// per-circuit solution state (see SolutionStateService for why Scoped).
builder.Services.AddScoped<ISolutionParser, BuildalyzerSolutionParser>();
builder.Services.AddScoped<IDependencyGraphBuilder, DependencyGraphBuilder>();
builder.Services.AddScoped<IUsageScanner, RoslynUsageScanner>();
builder.Services.AddScoped<IReportExporter, MarkdownJsonReportExporter>();
builder.Services.AddScoped<IGraphExporter, DotMermaidGraphExporter>();
builder.Services.AddScoped<SolutionStateService>();

// Singleton, not Scoped like the services above: NuGetCompatibilityChecker holds no per-circuit
// state of its own (its only state is a deliberately process-lifetime result cache - see its doc
// comment), so one shared instance across every circuit is simpler than a new one per connection
// for no benefit.
builder.Services.AddSingleton<INuGetCompatibilityChecker, NuGetCompatibilityChecker>();

// Same rationale as NuGetCompatibilityChecker above: the recent-workspaces JSON file and the
// ad-hoc source searcher hold no per-circuit state, so one shared singleton instance is simpler
// than a new one per connection for no benefit. JsonFileRecentWorkspacesStore defaults to
// %LocalAppData%/DotNetModAssess/recent-workspaces.json when constructed with no arguments.
builder.Services.AddSingleton<IRecentWorkspacesStore, JsonFileRecentWorkspacesStore>();
builder.Services.AddSingleton<IAdHocSourceSearcher, AdHocSourceSearcher>();

// Legacy-pattern (WCF/WPF/ConfigurationManager/AppDomain/COM Interop) migration-blocker
// detectors, fanned out by LegacyPatternScanner - a separate, complementary scan from the
// generic project/package usage scanner above. See LegacyPatterns/ILegacyPatternDetector.cs.
builder.Services.AddScoped<ILegacyPatternDetector, WcfPatternDetector>();
builder.Services.AddScoped<ILegacyPatternDetector, WpfPatternDetector>();
builder.Services.AddScoped<ILegacyPatternDetector, ConfigurationManagerPatternDetector>();
builder.Services.AddScoped<ILegacyPatternDetector, AppDomainPatternDetector>();
builder.Services.AddScoped<ILegacyPatternDetector, ComInteropPatternDetector>();
builder.Services.AddScoped<ILegacyPatternScanner, LegacyPatternScanner>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
