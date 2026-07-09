using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.LegacyPatterns;

/// <summary>
/// Detects usage of one specific, well-known .NET Framework/Windows-coupled legacy pattern (WCF,
/// WPF, ConfigurationManager, AppDomain, COM interop, etc.) across a solution, using whatever
/// detection strategy actually fits that pattern (attribute matching, specific member-access
/// shapes, project-level signals already captured during parsing, ...) - deliberately not a
/// generic "search for this namespace" scan, since the value of this family of detectors is
/// flagging the *specific* idioms that make each pattern a real migration blocker (e.g. a class
/// that *is* a WCF service contract, not just a file that happens to mention System.ServiceModel).
///
/// Implementations are combined by <see cref="ILegacyPatternScanner"/> - adding a new legacy
/// pattern means adding a new class implementing this interface (and registering it in DI), never
/// modifying existing detectors or the scanner (open/closed).
///
/// Results reuse the same <see cref="UsageResult"/>/<see cref="UsageConfidence"/> shape the
/// project/package usage scanner uses, with <see cref="UsageResult.TargetName"/> set to
/// <see cref="PatternName"/> rather than a project namespace or NuGet package id - it's the same
/// "what is this attributed to" concept, just a different kind of target.
/// </summary>
public interface ILegacyPatternDetector
{
    /// <summary>Short, stable identifier shown as <see cref="UsageResult.TargetName"/> and in
    /// reports/UI, e.g. "WCF", "ConfigurationManager", "AppDomain".</summary>
    string PatternName { get; }

    /// <summary>One-line, human-readable explanation of what this pattern flags and why it matters
    /// for a .NET Framework -> modern .NET on Linux containers migration (e.g. "AppDomains do not
    /// exist in .NET Core/5+ - any usage is a hard blocker requiring redesign").</summary>
    string Description { get; }

    Task<IReadOnlyList<UsageResult>> DetectAsync(SolutionModel solution, CancellationToken cancellationToken = default);
}
