using System.Collections.Concurrent;
using System.Xml.Linq;

namespace DepTree;

public record PackageCompatibility(bool? SupportsNet8, bool? SupportsNet10);

public static class NuGetCompatibilityChecker
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly ConcurrentDictionary<string, PackageCompatibility> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    static NuGetCompatibilityChecker()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "dep-tree/1.0");
    }

    public static async Task<PackageCompatibility> CheckAsync(string packageId, string version)
    {
        var key = $"{packageId}/{version}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = await FetchAsync(packageId, version);
        _cache.TryAdd(key, result);
        return result;
    }

    public static bool IsCheckableVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        if (version[0] == '$') return false;                     // MSBuild variable
        if (version[0] == '[' || version[0] == '(') return false; // NuGet range notation
        if (version.Contains('*')) return false;                  // wildcard
        return true;
    }

    private static async Task<PackageCompatibility> FetchAsync(string id, string version)
    {
        try
        {
            var idLow  = id.ToLowerInvariant();
            var verLow = version.ToLowerInvariant();
            var url    = $"https://api.nuget.org/v3-flatcontainer/{idLow}/{verLow}/{idLow}.nuspec";
            var xml    = await _http.GetStringAsync(url);
            return ParseNuspec(xml);
        }
        catch
        {
            return new PackageCompatibility(null, null);
        }
    }

    private static PackageCompatibility ParseNuspec(string xml)
    {
        var doc = XDocument.Parse(xml);
        var ns  = doc.Root?.Name.Namespace ?? XNamespace.None;

        var groups = doc.Descendants(ns + "group").ToList();

        if (groups.Count == 0)
        {
            // No dependency groups → package targets all frameworks (no-dep or flat deps)
            return new PackageCompatibility(true, true);
        }

        var tfms = groups.Select(g => g.Attribute("targetFramework")?.Value ?? "").ToList();

        // An empty-TFM group is a catch-all → compatible everywhere
        if (tfms.Any(string.IsNullOrEmpty))
            return new PackageCompatibility(true, true);

        return new PackageCompatibility(tfms.Any(IsNet8Compatible), tfms.Any(IsNet10Compatible));
    }

    // .NET 8 is compatible with: net5-8, netcoreapp*, netstandard*
    private static bool IsNet8Compatible(string tfm)
    {
        if (string.IsNullOrEmpty(tfm)) return true;
        var t = tfm.ToLowerInvariant();

        if (t is "net5.0" or "net6.0" or "net7.0" or "net8.0") return true;
        if (t.StartsWith("net5.0-") || t.StartsWith("net6.0-") ||
            t.StartsWith("net7.0-") || t.StartsWith("net8.0-")) return true;
        if (t.StartsWith("netcoreapp")  || t.StartsWith(".netcoreapp"))  return true;
        if (t.StartsWith("netstandard") || t.StartsWith(".netstandard")) return true;

        return false;
    }

    // .NET 10 is compatible with everything .NET 8 is, plus net9/net10 explicit targets
    private static bool IsNet10Compatible(string tfm)
    {
        if (string.IsNullOrEmpty(tfm)) return true;
        var t = tfm.ToLowerInvariant();

        if (t is "net9.0" or "net10.0") return true;
        if (t.StartsWith("net9.0-") || t.StartsWith("net10.0-")) return true;

        return IsNet8Compatible(tfm);
    }
}
