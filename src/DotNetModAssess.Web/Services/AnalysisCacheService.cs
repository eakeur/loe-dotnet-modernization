using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetModAssess.Core.Graph;
using DotNetModAssess.Core.Models;
using Microsoft.JSInterop;

namespace DotNetModAssess.Web.Services;

/// <summary>Everything <see cref="SolutionStateService"/> needs to restore a loaded solution
/// without re-parsing/re-scanning it, plus the source-tree freshness stamp it was computed from.</summary>
public sealed record CachedSolutionAnalysis(
    SolutionModel Solution,
    DependencyGraph Graph,
    IReadOnlyList<UsageResult> UsageResults,
    IReadOnlyList<UsageResult> LegacyFindings,
    DateTime SourceStampUtc);

/// <summary>
/// Persists a loaded solution's analysis (<see cref="SolutionModel"/>, <see cref="DependencyGraph"/>,
/// usage/legacy findings) to the browser's localStorage, keyed by solution path, so reopening the
/// same solution - including after an app restart, since localStorage outlives the process - can
/// skip re-running MSBuild evaluation and the usage/legacy scans entirely as long as nothing under
/// the solution has changed since the cache was written (<see cref="SolutionStateService"/> decides
/// that by comparing freshness stamps before trusting a cache hit).
///
/// JS interop calls here are best-effort, same philosophy as <c>IRecentWorkspacesStore</c> in
/// <see cref="SolutionStateService"/>: a failure (localStorage quota exceeded, payload too large,
/// JS runtime not yet available for the circuit) only disables caching for that call, never the
/// underlying load it's wrapping.
/// </summary>
public sealed class AnalysisCacheService(IJSRuntime jsRuntime, ILogger<AnalysisCacheService> logger)
{
    private const string KeyPrefix = "dotnetmodassess.analysisCache::";

    // JS interop calls issued right as a circuit is (re)connecting - e.g. LoadSolutionAsync firing
    // from a click that lands before the SignalR circuit has fully attached - can hang indefinitely
    // rather than throw: the RemoteJSRuntime's pending-call promise never resolves if the connection
    // that would carry the response never comes up. Bounding every call here means a stalled JS
    // runtime degrades to "skip the cache this time", never "the whole solution load never finishes".
    private static readonly TimeSpan JsCallTimeout = TimeSpan.FromSeconds(3);

    // ReferenceHandler.IgnoreCycles rather than the default: ProjectModel.ProjectReferences holds
    // nested ProjectModel instances (not just paths), and while a valid solution's reference graph
    // is a DAG, a malformed one could contain a real cycle - IgnoreCycles serializes those safely
    // (nulling the back-reference) instead of throwing, at the cost of exact reference-identity
    // round-tripping, which nothing downstream relies on (see ProjectModel/DependencyGraph consumers,
    // which key off Path/Id strings, not object identity).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(), new GraphNodeJsonConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    // DependencyGraph.Nodes is IReadOnlyList<GraphNode>, an abstract base with two sealed
    // subtypes (ProjectGraphNode/PackageGraphNode) - System.Text.Json can't deserialize an
    // abstract/interface type on its own, so a plain JsonSerializer.Deserialize<CachedSolutionAnalysis>
    // throws NotSupportedException on every read (writes succeed silently, since serializing a
    // concrete instance through its base type is fine - only deserialization needs help). This
    // converter uses the existing Kind discriminator to pick the concrete subtype to deserialize into.
    private sealed class GraphNodeJsonConverter : JsonConverter<GraphNode>
    {
        public override GraphNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var kind = doc.RootElement.GetProperty("kind").GetString();
            var raw = doc.RootElement.GetRawText();
            return kind switch
            {
                nameof(GraphNodeKind.Project) => JsonSerializer.Deserialize<ProjectGraphNode>(raw, options),
                nameof(GraphNodeKind.Package) => JsonSerializer.Deserialize<PackageGraphNode>(raw, options),
                _ => throw new JsonException($"Unknown graph node kind '{kind}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, GraphNode value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case ProjectGraphNode node:
                    JsonSerializer.Serialize(writer, node, options);
                    break;
                case PackageGraphNode node:
                    JsonSerializer.Serialize(writer, node, options);
                    break;
                default:
                    throw new JsonException($"Unknown graph node type '{value.GetType()}'.");
            }
        }
    }

    private IJSObjectReference? _module;

    public async Task<CachedSolutionAnalysis?> TryGetAsync(
        string solutionPath, DateTime currentSourceStampUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(JsCallTimeout);

            var module = await GetModuleAsync(cts.Token);
            var json = await module.InvokeAsync<string?>("getItem", cts.Token, CacheKey(solutionPath));
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var cached = JsonSerializer.Deserialize<CachedSolutionAnalysis>(json, JsonOptions);
            if (cached is null || cached.SourceStampUtc != currentSourceStampUtc)
            {
                return null;
            }

            return cached;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read cached analysis for '{SolutionPath}'; falling back to a full re-scan.", solutionPath);
            return null;
        }
    }

    public async Task SaveAsync(string solutionPath, CachedSolutionAnalysis analysis, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(JsCallTimeout);

            var module = await GetModuleAsync(cts.Token);
            var json = JsonSerializer.Serialize(analysis, JsonOptions);
            await module.InvokeVoidAsync("setItem", cts.Token, CacheKey(solutionPath), json);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to cache analysis for '{SolutionPath}'.", solutionPath);
        }
    }

    public async Task InvalidateAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(JsCallTimeout);

            var module = await GetModuleAsync(cts.Token);
            await module.InvokeVoidAsync("removeItem", cts.Token, CacheKey(solutionPath));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to invalidate cached analysis for '{SolutionPath}'.", solutionPath);
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken) =>
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./js/analysisCache.js");

    private static string CacheKey(string solutionPath) => KeyPrefix + solutionPath;
}
