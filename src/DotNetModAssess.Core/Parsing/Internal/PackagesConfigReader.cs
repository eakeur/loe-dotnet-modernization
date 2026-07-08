using System.Xml.Linq;

namespace DotNetModAssess.Core.Parsing.Internal;

internal sealed record PackagesConfigEntry(string PackageId, string Version, string? TargetFramework);

internal static class PackagesConfigReader
{
    public static IReadOnlyList<PackagesConfigEntry> Read(string packagesConfigPath)
    {
        if (!File.Exists(packagesConfigPath))
        {
            return [];
        }

        var doc = XDocument.Load(packagesConfigPath);
        return doc.Root?
            .Elements("package")
            .Select(e => new PackagesConfigEntry(
                e.Attribute("id")?.Value ?? string.Empty,
                e.Attribute("version")?.Value ?? string.Empty,
                e.Attribute("targetFramework")?.Value))
            .Where(p => !string.IsNullOrWhiteSpace(p.PackageId))
            .ToList()
            ?? [];
    }
}
