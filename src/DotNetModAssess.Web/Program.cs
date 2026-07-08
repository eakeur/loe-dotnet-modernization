using DotNetModAssess.Core.Parsing;
using DotNetModAssess.Web.Components;
using DotNetModAssess.Web.Parsing;
using DotNetModAssess.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Fixture-backed parser + per-circuit solution state (see SolutionStateService for why Scoped).
builder.Services.AddScoped<ISolutionParser, FixtureBackedSolutionParser>();
builder.Services.AddScoped<SolutionStateService>();

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
