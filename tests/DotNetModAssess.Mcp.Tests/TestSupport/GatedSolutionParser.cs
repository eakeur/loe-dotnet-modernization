using DotNetModAssess.Core.Models;
using DotNetModAssess.Core.Parsing;

namespace DotNetModAssess.Mcp.Tests.TestSupport;

/// <summary>
/// Wraps a real <see cref="ISolutionParser"/> but blocks inside <see cref="ParseAsync"/> until a
/// test explicitly calls <see cref="Release"/>. This is what lets tests deterministically exercise
/// "a data tool is called while the workspace is still loading" without racing against how fast the
/// real (tiny) fixture solution happens to parse on whatever machine the tests run on - the test
/// controls the exact moment loading is allowed to complete.
/// </summary>
internal sealed class GatedSolutionParser(ISolutionParser inner) : ISolutionParser
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Allows the wrapped real parse to proceed.</summary>
    public void Release() => _gate.TrySetResult();

    public async Task<SolutionModel> ParseAsync(string solutionPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("(test) waiting for gate to be released...");
        await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await inner.ParseAsync(solutionPath, progress, cancellationToken).ConfigureAwait(false);
    }
}
