using Buildalyzer;
using Microsoft.Build.Framework;

namespace DotNetModAssess.Core.Parsing.Internal;

/// <summary>
/// The outcome of attempting to evaluate/build a single project via Buildalyzer. This is
/// intentionally separate from ProjectEvaluationStatus (the public model type) because it also
/// carries the raw IAnalyzerResult (when one could be obtained at all) for metadata mapping, even
/// in cases where the evaluation ultimately did not fully succeed.
/// </summary>
internal sealed record ProjectEvaluationOutcome(
    bool Succeeded,
    string? ErrorMessage,
    IAnalyzerResult? Result,
    IReadOnlyList<BuildEventArgs> BuildEvents)
{
    public static ProjectEvaluationOutcome Empty(string message) => new(false, message, null, []);
}

/// <summary>
/// Wraps Buildalyzer evaluation of a single project, catching any exception so that one
/// project's evaluation failure (e.g. a legacy net48/packages.config project on a machine with
/// no full-framework MSBuild toolchain, or a project with a broken ProjectReference) can never
/// abort the scan of the rest of the solution.
/// </summary>
internal static class ProjectEvaluator
{
    public static ProjectEvaluationOutcome Evaluate(IAnalyzerManager manager, string projectPath)
    {
        try
        {
            MSBuildEnvironmentInitializer.EnsureRegistered();

            var analyzer = manager.GetProject(projectPath);
            var results = analyzer.Build();
            var buildEvents = results.BuildEventArguments.IsDefault ? [] : (IReadOnlyList<BuildEventArgs>)results.BuildEventArguments;
            var result = results.Results.FirstOrDefault();

            if (result is null)
            {
                return new ProjectEvaluationOutcome(false, "Buildalyzer produced no analyzer result for this project.", null, buildEvents);
            }

            if (!result.Succeeded)
            {
                return new ProjectEvaluationOutcome(false, BuildFailureSummary(projectPath), result, buildEvents);
            }

            return new ProjectEvaluationOutcome(true, null, result, buildEvents);
        }
        catch (Exception ex)
        {
            return ProjectEvaluationOutcome.Empty($"MSBuild evaluation of '{projectPath}' failed: {ex.Message}");
        }
    }

    private static string BuildFailureSummary(string projectPath) =>
        $"MSBuild evaluation of '{projectPath}' did not succeed (build reported failure). See Metadata.Diagnostics for details.";
}
