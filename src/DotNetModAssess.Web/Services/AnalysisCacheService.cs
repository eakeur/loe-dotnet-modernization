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

    // ReferenceHandler.IgnoreCycles rather than the default: ProjectModel.ProjectReferences holds
    // nested ProjectModel instances (not just paths), and while a valid solution's reference graph
    // is a DAG, a malformed one could contain a real cycle - IgnoreCycles serializes those safely
    // (nulling the back-reference) instead of throwing, at the cost of exact reference-identity
    // round-tripping, which nothing downstream relies on (see ProjectModel/DependencyGraph consumers,
    // which key off Path/Id strings, not object identity).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    private IJSObjectReference? _module;

    public async Task<CachedSolutionAnalysis?> TryGetAsync(
        string solutionPath, DateTime currentSourceStampUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await GetModuleAsync();
            var json = await module.InvokeAsync<string?>("getItem", cancellationToken, CacheKey(solutionPath));
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
            var module = await GetModuleAsync();
            var json = JsonSerializer.Serialize(analysis, JsonOptions);
            await module.InvokeVoidAsync("setItem", cancellationToken, CacheKey(solutionPath), json);
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
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("removeItem", cancellationToken, CacheKey(solutionPath));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to invalidate cached analysis for '{SolutionPath}'.", solutionPath);
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync() =>
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/analysisCache.js");

    private static string CacheKey(string solutionPath) => KeyPrefix + solutionPath;
}
