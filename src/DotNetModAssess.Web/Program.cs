using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.LegacyPatterns;
using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Core.Reporting;
using DotNetModAssess.Core.UsageScanning;
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
