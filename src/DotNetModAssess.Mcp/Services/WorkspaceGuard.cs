using DotNetModAssess.Core.Models;
using ModelContextProtocol;

namespace DotNetModAssess.Mcp.Services;

/// <summary>
/// Shared "await until ready, then fail loudly and clearly if there's nothing to return" logic used
/// by every data-returning tool. Centralizing this means every tool gets the exact same behavior
/// for the three interesting cases (loaded, still loading somehow after the await - shouldn't
/// happen but defensive, and failed) instead of each tool reinventing its own null-check.
/// </summary>
public static class WorkspaceGuard
{
    /// <summary>
    /// Awaits <see cref="McpWorkspaceState.LoadingTask"/> - this is the mechanism that makes every
    /// data tool transparently wait for a still-in-progress load rather than needing its own
    /// retry/poll logic: if loading already finished by the time this is called (the common case),
    /// this returns immediately; if called too early, it blocks until the in-flight load
    /// completes. Throws a client-visible <see cref="McpException"/> if there is no loaded solution
    /// to return afterwards (the initial load failed, or hasn't produced anything yet).
    /// </summary>
    public static async Task<SolutionModel> EnsureLoadedAsync(McpWorkspaceState state, CancellationToken cancellationToken = default)
    {
        await state.LoadingTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (state.Solution is not null)
        {
            return state.Solution;
        }

        if (state.Status == WorkspaceLoadStatus.Failed)
        {
            throw new McpException(
                $"Loading the workspace failed: {state.LastError ?? "unknown error"}. " +
                "Call get_load_status for details, or reload to try again.");
        }

        throw new McpException(
            "No solution is currently loaded. Call get_load_status to check progress, or reload to try again.");
    }
}
