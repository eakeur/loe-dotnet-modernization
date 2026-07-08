using System.Xml.Linq;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Parsing.Internal;

/// <summary>
/// A raw project reference discovered directly from project XML (i.e. not dependent on
/// MSBuild evaluation succeeding).
/// </summary>
internal sealed record RawProjectReference(string RawInclude, string ResolvedPath);

/// <summary>
/// A raw PackageReference item as it appears in the project file, before any central
/// package management substitution.
/// </summary>
internal sealed record RawPackageReference(string PackageId, string? Version);

/// <summary>
/// A raw legacy-style &lt;Reference&gt; item (as opposed to PackageReference).
/// </summary>
internal sealed record RawAssemblyReference(string AssemblyName, string? HintPath);

/// <summary>
/// Everything we can determine about a project purely by reading its XML on disk, with no
/// MSBuild evaluation involved. This is intentionally evaluation-independent so that format
/// detection and reference resolution keep working even when full MSBuild evaluation fails
/// (e.g. a legacy net48 project on a machine with no full-framework MSBuild toolchain).
/// </summary>
internal sealed class RawProjectFile
{
    public required string Path { get; init; }
    public required ProjectFormat Format { get; init; }
    public string? ToolsVersion { get; init; }
    public required IReadOnlyList<string> TargetFrameworksRaw { get; init; }
    public required IReadOnlyList<RawProjectReference> ProjectReferences { get; init; }
    public required IReadOnlyList<RawPackageReference> PackageReferences { get; init; }
    public required IReadOnlyList<RawAssemblyReference> AssemblyReferences { get; init; }
    public required IReadOnlyList<string> ComReferences { get; init; }
    public required IReadOnlyList<CustomBuildElement> CustomTargetsAndImports { get; init; }
    public bool HasPackagesConfig { get; init; }
    public bool HasAppConfig { get; init; }
    public bool HasWebConfig { get; init; }
    public bool HasWebConfigTransforms { get; init; }
    public string? OutputTypeRaw { get; init; }
    public string? AssemblyNameRaw { get; init; }
    public string? RootNamespaceRaw { get; init; }

    private static readonly string[] StandardImportMarkers =
    [
        "Microsoft.Common.props",
        "Microsoft.Common.targets",
        "Microsoft.CSharp.targets",
        "Microsoft.VisualBasic.targets",
        "Microsoft.FSharp.targets",
        "Sdk.props",
        "Sdk.targets",
        "NuGet.targets",
    ];

    private static readonly HashSet<string> WindowsOnlyAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Windows.Forms",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Drawing",
        "System.DirectoryServices",
        "Microsoft.Win32",
        "UIAutomationClient",
        "UIAutomationTypes",
    };

    private static readonly HashSet<string> LegacyCouplingAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Web",
        "System.ServiceModel",
        "System.Messaging",
    };

    /// <summary>
    /// Legacy (non-SDK-style) project files declare a default XML namespace
    /// (xmlns="http://schemas.microsoft.com/developer/msbuild/2003") on the root &lt;Project&gt;
    /// element; SDK-style project files declare none. <see cref="XContainer.Descendants(XName)"/>
    /// only matches elements in the *exact* namespace requested, so a plain unqualified name like
    /// "Reference" silently matches nothing in a legacy project file. We look up elements by local
    /// name instead so the same code works for both project styles.
    /// </summary>
    private static IEnumerable<XElement> DescendantsByLocalName(XElement root, string localName) =>
        root.Descendants().Where(e => e.Name.LocalName == localName);

    private static XElement? ChildByLocalName(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    public static RawProjectFile Load(string projectPath)
    {
        var doc = XDocument.Load(projectPath, LoadOptions.None);
        var root = doc.Root ?? throw new InvalidOperationException($"Project file '{projectPath}' has no root element.");
        var projectDirectory = System.IO.Path.GetDirectoryName(projectPath)!;

        var sdkAttr = root.Attribute("Sdk")?.Value;
        var toolsVersion = root.Attribute("ToolsVersion")?.Value;
        var format = !string.IsNullOrWhiteSpace(sdkAttr)
            ? ProjectFormat.SdkStyle
            : !string.IsNullOrWhiteSpace(toolsVersion)
                ? ProjectFormat.LegacyStyle
                : ProjectFormat.Unknown;

        var tfmRaw = new List<string>();
        var tfElement = DescendantsByLocalName(root, "TargetFramework").FirstOrDefault()?.Value;
        var tfsElement = DescendantsByLocalName(root, "TargetFrameworks").FirstOrDefault()?.Value;
        var tfvElement = DescendantsByLocalName(root, "TargetFrameworkVersion").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(tfsElement))
        {
            tfmRaw.AddRange(tfsElement.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else if (!string.IsNullOrWhiteSpace(tfElement))
        {
            tfmRaw.Add(tfElement.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(tfvElement))
        {
            tfmRaw.Add(tfvElement.Trim());
        }

        var projectReferences = DescendantsByLocalName(root, "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => new RawProjectReference(v!, NormalizePath(System.IO.Path.Combine(projectDirectory, v!.Replace('\\', System.IO.Path.DirectorySeparatorChar)))))
            .ToList();

        var packageReferences = DescendantsByLocalName(root, "PackageReference")
            .Select(e => new RawPackageReference(
                e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? string.Empty,
                e.Attribute("Version")?.Value ?? ChildByLocalName(e, "Version")?.Value))
            .Where(p => !string.IsNullOrWhiteSpace(p.PackageId))
            .ToList();

        var assemblyReferences = DescendantsByLocalName(root, "Reference")
            .Select(e => new RawAssemblyReference(
                (e.Attribute("Include")?.Value ?? string.Empty).Split(',')[0].Trim(),
                ChildByLocalName(e, "HintPath")?.Value))
            .Where(r => !string.IsNullOrWhiteSpace(r.AssemblyName))
            .ToList();

        var comReferences = DescendantsByLocalName(root, "COMReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        var customElements = new List<CustomBuildElement>();
        foreach (var import in DescendantsByLocalName(root, "Import"))
        {
            var projectAttr = import.Attribute("Project")?.Value;
            if (string.IsNullOrWhiteSpace(projectAttr)) continue;
            if (StandardImportMarkers.Any(marker => projectAttr.Contains(marker, StringComparison.OrdinalIgnoreCase))) continue;
            customElements.Add(new CustomBuildElement { ElementType = "Import", Name = projectAttr, DefiningFile = projectPath });
        }
        foreach (var target in DescendantsByLocalName(root, "Target"))
        {
            var nameAttr = target.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(nameAttr)) continue;
            customElements.Add(new CustomBuildElement { ElementType = "Target", Name = nameAttr, DefiningFile = projectPath });
        }

        var hasPackagesConfig = File.Exists(System.IO.Path.Combine(projectDirectory, "packages.config"));
        var hasAppConfig = File.Exists(System.IO.Path.Combine(projectDirectory, "App.config"))
            || File.Exists(System.IO.Path.Combine(projectDirectory, "app.config"));
        var hasWebConfig = File.Exists(System.IO.Path.Combine(projectDirectory, "Web.config"))
            || File.Exists(System.IO.Path.Combine(projectDirectory, "web.config"));
        var hasWebConfigTransforms = Directory.Exists(projectDirectory) && Directory.EnumerateFiles(projectDirectory, "*.config")
            .Any(f =>
            {
                var name = System.IO.Path.GetFileName(f);
                return (name.Contains("Web.", StringComparison.OrdinalIgnoreCase) || name.Contains("App.", StringComparison.OrdinalIgnoreCase))
                    && !name.Equals("Web.config", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("App.config", StringComparison.OrdinalIgnoreCase);
            });

        return new RawProjectFile
        {
            Path = projectPath,
            Format = format,
            ToolsVersion = toolsVersion,
            TargetFrameworksRaw = tfmRaw,
            ProjectReferences = projectReferences,
            PackageReferences = packageReferences,
            AssemblyReferences = assemblyReferences,
            ComReferences = comReferences,
            CustomTargetsAndImports = customElements,
            HasPackagesConfig = hasPackagesConfig,
            HasAppConfig = hasAppConfig,
            HasWebConfig = hasWebConfig,
            HasWebConfigTransforms = hasWebConfigTransforms,
            OutputTypeRaw = DescendantsByLocalName(root, "OutputType").FirstOrDefault()?.Value,
            AssemblyNameRaw = DescendantsByLocalName(root, "AssemblyName").FirstOrDefault()?.Value,
            RootNamespaceRaw = DescendantsByLocalName(root, "RootNamespace").FirstOrDefault()?.Value,
        };
    }

    public bool ReferencesSystemWeb => AssemblyReferences.Any(r => r.AssemblyName.Equals("System.Web", StringComparison.OrdinalIgnoreCase));

    public bool ReferencesSystemServiceModel => AssemblyReferences.Any(r => r.AssemblyName.Equals("System.ServiceModel", StringComparison.OrdinalIgnoreCase));

    public bool ReferencesSystemMessaging => AssemblyReferences.Any(r => r.AssemblyName.Equals("System.Messaging", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> LegacyAssemblyReferences => AssemblyReferences
        .Select(r => r.AssemblyName)
        .Where(name => LegacyCouplingAssemblies.Contains(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> WindowsOnlyAssemblyReferences => AssemblyReferences
        .Select(r => r.AssemblyName)
        .Where(name => WindowsOnlyAssemblies.Contains(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string NormalizePath(string path) =>
        System.IO.Path.GetFullPath(path);
}
