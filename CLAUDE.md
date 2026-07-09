# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

DotNetModAssess ingests a legacy .NET solution (a mix of .NET Framework and SDK-style projects) and helps assess modernizing it to run on modern .NET / Linux containers. It parses the solution via real MSBuild evaluation, builds a dependency graph, scans source for project/package usage and known legacy-API patterns (WCF, WPF, ConfigurationManager, AppDomain, COM interop), and checks NuGet packages for .NET 8-compatible versions. All of that logic lives in `DotNetModAssess.Core`; there are two independent, UI-agnostic consumers of it: a Blazor Server web UI (`DotNetModAssess.Web`) for a human assessor, and an MCP (Model Context Protocol) stdio server (`DotNetModAssess.Mcp`) exposing the same findings as tools for an AI coding agent.

The app is **designed to run on Windows** (full-fidelity MSBuild evaluation of legacy non-SDK-style projects is most reliable against the Windows MSBuild/VS Build Tools toolchain), but is developed cross-platform. On a non-Windows dev machine, evaluation of a legacy net48-style project may gracefully report `ProjectEvaluationStatus.Failed` rather than fully succeeding — this is expected and handled by design, not a bug to fix. Tests that exercise this are written to tolerate either outcome.

## Solution layout

```
DotNetModAssess.sln
src/
  DotNetModAssess.Core/    classlib, net8.0 — all parsing/graph/analysis logic
  DotNetModAssess.Web/     Blazor Server (net8.0), consumes Core
  DotNetModAssess.Mcp/     console app, net8.0, MCP stdio server, consumes Core
tests/
  DotNetModAssess.Core.Tests/   xUnit
  DotNetModAssess.Mcp.Tests/    xUnit
fixtures/
  SampleLegacySolution/         real, hand-built solution (legacy net48 + SDK-style + packages.config +
                                 CPM) used for integration tests of the real parser
  sample-solution-basic/        flat JSON fixtures (no real files) for fast unit tests
```

Everything was scaffolded via `dotnet new`/`dotnet sln add`/`dotnet add package`/`dotnet add reference` — never hand-author project references or package versions in the `.csproj` files; use the CLI and let it edit the XML. Manual XML edits are reserved for settings (`ExcludeAssets`, `PrivateAssets`, etc.), not for adding references.

## Commands

```bash
dotnet build DotNetModAssess.sln
dotnet test DotNetModAssess.sln

# single test class or method
dotnet test --filter "FullyQualifiedName~RoslynUsageScannerTests"
dotnet test --filter "FullyQualifiedName~RoslynUsageScannerTests.ScanAsync_DetectsUsingDirectivesForPackageAndProjectTargets"

# run the web app
dotnet run --project src/DotNetModAssess.Web

# run the MCP server directly (normally launched by an MCP client instead, e.g. Claude Code's
# .mcp.json - the path argument is required, not optional)
dotnet run --project src/DotNetModAssess.Mcp -- /path/to/MySolution.sln
```

The SDK is pinned via `global.json` (8.0.402) — run `dotnet --list-sdks` if a build fails for an SDK-mismatch reason.

## Architecture

### Domain model → graph → analysis, as separate stages

`SolutionModel` (path, `ProjectModel[]`, NuGet sources, `Directory.Build.*`/CPM info) is the root object everything else is built from. `ProjectModel.ProjectReferences` holds real nested `ProjectModel` objects (not paths) — building this graph requires resolving projects in dependency order; `Parsing/Internal/SolutionGraphBuilder.cs` does this via **memoized recursion** (a project is built once, referencing projects reuse the same instance). A dangling `ProjectReference` (file doesn't exist) is never a hard failure for the referencing project — it's recorded as an `EvaluationDiagnostic` and omitted from `ProjectReferences`, since MSBuild itself doesn't consistently fail on this either.

`DependencyGraph` (nodes: `ProjectGraphNode`/`PackageGraphNode`, edges: `GraphEdge`) is a *separate* structure built from a `SolutionModel` by `IDependencyGraphBuilder` — it's not part of the parser's output. Package nodes are deduplicated across the whole solution and flagged `HasVersionConflict` when the same package id resolves to different versions in different projects.

### Parsing performance: pre-warm in parallel, then link sequentially

`BuildalyzerSolutionParser.ParseAsync` evaluates every directly-discovered project **in parallel** (bounded `Parallel.ForEachAsync`) to populate `Parsing/Internal/EvaluationCache` (a static, process-lifetime cache keyed by `(path, last-write-time)`), then runs the sequential, dependency-ordered `SolutionGraphBuilder` pass, which reads from that now-warm cache instead of re-evaluating. This is also what makes the Web layer's live-watch re-scan fast: touching one `.csproj` and re-parsing the whole solution only pays real MSBuild cost for the changed project — everything else is a cache hit.

### Usage scanning is three independent passes, unioned and deduplicated by target identity

`IUsageScanner` (`RoslynUsageScanner`) answers "how much is this project/package actually used elsewhere in the solution":

1. **`RoslynUsageScanner` itself** — Roslyn syntax-tree matching (`using` directives, fully-qualified type references, member access, attributes, base lists) against each project's approximate root namespace or each package id. Tags `UsageConfidence.Confirmed`. Cannot see bare/unqualified identifiers (`using System.Web;` then a later bare `HttpContext.Current`) — that requires a compilation/symbol model, out of scope by design.
2. **`TextSearchUsageScanner`** — plain word-boundary regex search for each target's literal name across a broader file set (`.razor`/`.cshtml`/`.config`/`.json`/`.xml`, not just `.cs`). Tags `UsageConfidence.TextMatch`.
3. **`SuffixHarvestUsageScanner`** — harvests progressively-shortened suffixes from *Confirmed* results (`System.Web.HttpContext` → also search for `HttpContext`) and searches for those too, to catch the bare-identifier case #1 can't. Weakest tier, `UsageConfidence.SuffixMatch`.

Every `UsageResult` carries `TargetName` (which project namespace or package id it's attributed to) *in addition to* `MatchedSymbol` (the literal text actually found) — the two differ because a Confirmed/SuffixMatch hit's matched text is often longer/shorter than the target's own name. **Deduplication and precedence (Confirmed > TextMatch > SuffixMatch) are keyed on `(FilePath, LineNumber, TargetName)`, never on `MatchedSymbol`** — an earlier version of this keyed on `MatchedSymbol` and double-reported the same line under two tiers, since a Confirmed match's matched text and a TextMatch's matched text are literally different strings for the same underlying occurrence.

### Legacy-pattern detection is a separate, open/closed scan

`ILegacyPatternScanner`/`ILegacyPatternDetector` answer a different question than the usage scanner: "does this solution use any of these specific, well-known migration blockers" (WCF, WPF, ConfigurationManager, AppDomain, COM interop), each with a tailored detection algorithm rather than a generic namespace search (e.g. WCF looks for `[ServiceContract]`/`ChannelFactory<T>`, not just "mentions System.ServiceModel"). Adding a new pattern means adding a new `ILegacyPatternDetector` implementation and registering it in `Program.cs` — `LegacyPatternScanner` just fans out to every registered detector via `IEnumerable<ILegacyPatternDetector>` (multiple DI registrations of the same interface). Findings reuse the same `UsageResult`/`UsageConfidence` shape as the usage scanner, with `TargetName` set to the pattern name instead of a project/package.

This is complementary to, not a replacement for, the boolean flags on `ProjectModel.Metadata.LegacySignals` (`ReferencesSystemWeb`, `UsesConfigurationManager`, etc.) set during parsing — those are cheap per-project signals, the detectors add file:line-level evidence.

### NuGet compatibility checking splits network I/O from decision logic

`PackageCompatibility/NuGetCompatibilityChecker` uses the real `NuGet.Protocol` client (not raw HTTP against nuget.org) so it respects whatever sources are configured (private feeds, auth) — reusing `Parsing/Internal/NuGetConfigResolver`'s source discovery. Its actual "is the current version compatible, and if not what's the minimum compatible upgrade" decision logic is duplicated in a separate, synchronous, fully unit-testable class, `NuGetVersionCompatibilityEvaluator` (the real checker can't call it directly because the real per-version compatibility check is an async network call, and `NuGetVersionCompatibilityEvaluator` takes a synchronous predicate — see that class's doc comment). If you change the upgrade-recommendation algorithm, change it in both places, or better, look at whether the async/sync split can be removed.

Compatibility results are cached process-lifetime, keyed by `(packageId, version, tfm)`, with no expiry (published package metadata doesn't change retroactively) — only successful checks are cached, so a transient network failure doesn't stick.

### `SolutionStateService` is the Web layer's single state hub

Scoped per Blazor circuit (one per connected browser tab, so different assessors/tabs never share mutable state). Owns the currently-loaded `SolutionModel`/`DependencyGraph`/`UsageResults`/`LegacyFindings` (the last computed eagerly alongside the others via `ILegacyPatternScanner`, so every panel that needs it doesn't re-scan), exposes a `Changed` event every panel subscribes to for re-render, records every successful load in `IRecentWorkspacesStore` (best-effort — a failure there never fails the load), and — once a solution is loaded — starts a debounced `FileSystemWatcher` on the solution's directory tree that automatically re-parses on any `.sln`/`.slnf`/`.csproj`/`Directory.Build.*`/`Directory.Packages.props`/`NuGet.Config` change (this is the "live" re-scan feature; it's cheap thanks to the evaluation cache described above). `CloseSolution()` resets state for the workspace-switcher flow back to the landing screen.

### Web UI is a VS Code-style workbench, not a set of independent pages

`MainLayout.razor` renders a persistent shell — `ActivityBar` (far-left icon strip: Explorer/Search/Findings, the last badge-counted from `LegacyFindings`) + `SideBar` (filterable project/package tree, stays mounted across navigation) + `StatusBar`, wrapping `@Body`. Routes under `/workspace/...` (`/workspace`, `/workspace/project/{*ProjectPath}`, `/workspace/package/{*PackageId}`, `/workspace/findings`, `/workspace/search`, `/workspace/file`, `/workspace/graph`) swap the main-panel content inside that shell; `/` is the landing/recent-workspaces screen shown when no solution is loaded. Project/package detail panels can show the dependency graph *rooted at that node* by passing `DependencyGraphView`'s `InitialFocusNodeId` parameter, which reuses the exact same click-to-focus highlight logic (`OnNodeClicked`) a real click would trigger, rather than duplicating it. The read-only code viewer (`Workspace/FileViewer.razor`) drives the Monaco Editor (loaded via pinned CDN script in `App.razor`, same pattern as Cytoscape.js) through `wwwroot/js/codeViewer.js`.

### `DotNetModAssess.Mcp` is a third thin presentation layer, not a rearchitecture

Same relationship to Core as the Web project - a new consumer, zero changes to Core's contracts. Registers every Core service as **Singleton** (not Scoped like Web's per-circuit registrations): the process serves exactly one loaded workspace for its whole lifetime by design, there's no "per connection" concept to scope to. The solution/filter/project path is a **required command-line argument** (`args[0]`), not a tool parameter - set once in the MCP client's config (e.g. Claude Code's `.mcp.json`), not chosen interactively by the agent.

The one requirement that shapes everything else here: an LLM calling a tool must never block on a multi-minute solution parse as part of its own turn. `Program.cs` fires `McpWorkspaceState.StartAsync(path)` as a background `Task` *before* `host.RunAsync()` starts the MCP protocol loop, so the `initialize` handshake is always instant regardless of solution size. Every data-returning tool `await`s `McpWorkspaceState.LoadingTask` internally - if a tool is called after loading already finished (the common case, given real wall-clock time passes between "server starts" and "agent calls a tool"), it returns instantly; if called too early, it transparently waits and then returns correct data, with no retry logic needed on the agent's side. `get_load_status` is the sole exception - it never awaits, so an agent can poll real progress (reusing the exact same `IProgress<string>` plumbing described above) instead of blindly waiting on a data tool. `McpWorkspaceState` is otherwise a process-wide cousin of `SolutionStateService` (same eager usage/legacy-pattern scan, same lazy `Graph`, same debounced `FileSystemWatcher` live-reload) with no Blazor `Changed` event to raise.

Tool methods return small DTOs local to the Mcp project (`Dtos/`), never Core domain types directly - same reasoning as `DependencyGraphView.razor`'s `GraphNodeDto`/`GraphEdgeDto` projection for JS interop, just for MCP's JSON-RPC serialization instead.

### Fixtures: two different strategies for two different needs

- `fixtures/sample-solution-basic/*.json` + `Core/Fixtures/FixtureDataLoader.cs` — no real files on disk, just flat DTOs matching the frozen domain model shapes, hydrated into real `SolutionModel`/`DependencyGraph` object graphs (with correct nested-reference identity, built via the same memoized-recursion pattern as `SolutionGraphBuilder`). Used where a test needs a realistic, cheap, deterministic `SolutionModel` without invoking Buildalyzer at all (e.g. graph-builder and reporting-exporter tests).
- `fixtures/SampleLegacySolution/` — an actual, tiny, hand-built solution on disk (real `.csproj`/`packages.config`/`Directory.Packages.props`) that Buildalyzer really evaluates, used by `BuildalyzerSolutionParserTests` to exercise the real parsing path end-to-end, including the legacy-project-evaluation-fails-gracefully-on-non-Windows case.
- `tests/DotNetModAssess.Core.Tests/{UsageScanningFixtures,LegacyPatternsFixtures}/*.cs` are **not compiled** into the test assembly (`<Compile Remove>` + `<None Include>` in the test `.csproj`) — they're fed to `CSharpSyntaxTree.ParseText` as syntax-only inputs for the scanners/detectors under test, and several intentionally reference types that don't actually exist (`ServiceHost`, `ChannelFactory<T>`, etc.), since only their syntax shape matters.

## Known NuGet version-pinning gotchas

Adding a package to `DotNetModAssess.Core` can silently reintroduce either of these; both were hit once already:

- `Microsoft.CodeAnalysis.CSharp` (direct) and `Buildalyzer` (which transitively pulls `Microsoft.CodeAnalysis.VisualBasic`) both depend on `Microsoft.CodeAnalysis.Common`, at incompatible pinned versions → `NU1107` build error. Fixed by adding an explicit direct `PackageReference` to `Microsoft.CodeAnalysis.Common` at the higher version, which wins via NuGet's nearest-wins resolution.
- Something in the `Buildalyzer`/`NuGet.Protocol` dependency chain transitively requires a specific `System.Text.Json` version different from the one the SDK's shared framework provides as "primary" — this one **builds clean** (only an `MSB3277` warning) but **throws `FileNotFoundException` at runtime** the first time reporting/export code actually calls `JsonSerializer`. Fixed the same way: an explicit direct `PackageReference` pin at the version transitively required (`dotnet list package --include-transitive` shows what that is).

If a new package addition produces an `NU1107`/`NU1608` or an `MSB3277` "found conflicts" warning, don't ignore it — check whether it's this same class of problem before it becomes a runtime-only failure.
