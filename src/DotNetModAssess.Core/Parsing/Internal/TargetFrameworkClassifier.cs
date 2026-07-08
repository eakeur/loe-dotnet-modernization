using System.Text.RegularExpressions;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Parsing.Internal;

internal static partial class TargetFrameworkClassifier
{
    /// <summary>
    /// Normalizes a legacy `TargetFrameworkVersion` value (e.g. "v4.8", "v4.7.2") into a
    /// modern-style moniker (e.g. "net48", "net472") so legacy and SDK-style projects can be
    /// compared/graphed consistently. Values that are already monikers are returned unchanged.
    /// </summary>
    public static string NormalizeToMoniker(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var match = LegacyVersionRegex().Match(raw);
        if (!match.Success)
        {
            return raw;
        }

        var digits = string.Concat(match.Groups["version"].Value.Split('.'));
        return "net" + digits;
    }

    public static TargetFrameworkClassification Classify(IReadOnlyList<string> rawTargetFrameworks)
    {
        if (rawTargetFrameworks.Count > 1)
        {
            return TargetFrameworkClassification.Multi;
        }

        if (rawTargetFrameworks.Count == 0)
        {
            return TargetFrameworkClassification.Unknown;
        }

        return ClassifySingle(rawTargetFrameworks[0]);
    }

    private static TargetFrameworkClassification ClassifySingle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return TargetFrameworkClassification.Unknown;
        }

        var moniker = NormalizeToMoniker(raw).ToLowerInvariant();

        if (moniker.StartsWith("netstandard", StringComparison.Ordinal))
        {
            return TargetFrameworkClassification.NetStandard;
        }

        if (moniker.StartsWith("netcoreapp", StringComparison.Ordinal))
        {
            return TargetFrameworkClassification.NetCoreApp;
        }

        if (moniker.StartsWith("net", StringComparison.Ordinal))
        {
            var versionPart = moniker["net".Length..];
            // Modern monikers look like "net8.0", "net9.0-windows", etc. (contain a '.').
            // Legacy monikers look like "net48", "net472", "net40" (no '.').
            var majorDigits = new string(versionPart.TakeWhile(char.IsDigit).ToArray());
            if (versionPart.Contains('.'))
            {
                return TargetFrameworkClassification.Modern;
            }

            if (majorDigits.Length > 0 && int.TryParse(majorDigits[..1], out var majorFirstDigit) && majorFirstDigit <= 4)
            {
                return TargetFrameworkClassification.NetFramework;
            }

            return TargetFrameworkClassification.Modern;
        }

        return TargetFrameworkClassification.Unknown;
    }

    [GeneratedRegex(@"^v?(?<version>\d+(\.\d+){1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyVersionRegex();
}
