using System.Xml;

namespace DepTree;

public static class ProjectParser
{
    private static readonly (string Pattern, string Note, bool IsHighRisk)[] RiskyPackages =
    [
        ("System.Web",              "System.Web not available on .NET Core/5+",            true),
        ("Microsoft.AspNet",        "ASP.NET (classic) not available on .NET Core/5+",     true),
        ("^EntityFramework$",       "EF6 - consider migrating to EF Core",                 false),
        ("Microsoft.Owin",          "OWIN middleware - may need Katana replacement",        false),
        ("WCF",                     "WCF server-side not supported on .NET Core/5+",        true),
        ("System.Messaging",        "MSMQ not available on .NET Core/5+",                  true),
        ("MSMQ",                    "MSMQ not available on .NET Core/5+",                  true),
        ("Microsoft.VisualBasic",   "Microsoft.VisualBasic compatibility layer needed",    false),
        ("System.Runtime.Remoting", ".NET Remoting not supported on .NET Core/5+",         true),
        ("Newtonsoft.Json",         "Consider migrating to System.Text.Json",              false),
    ];

    public static ProjectInfo Parse(string csprojPath, string solutionRoot)
    {
        var doc = new XmlDocument();
        doc.Load(csprojPath);

        var dir         = System.IO.Path.GetDirectoryName(csprojPath)!;
        var name        = System.IO.Path.GetFileNameWithoutExtension(csprojPath);
        var relativePath = System.IO.Path.GetRelativePath(solutionRoot, csprojPath);

        var frameworks   = GetTargetFrameworks(doc);
        var packages     = GetPackageReferences(doc);
        var projectRefs  = GetProjectReferences(doc, dir);
        var projectStyle = GetProjectStyle(doc);
        var risk         = GetMigrationRisk(frameworks, packages, projectStyle);

        return new ProjectInfo
        {
            Id               = csprojPath,
            Name             = name,
            Path             = csprojPath,
            RelativePath     = relativePath,
            ProjectStyle     = projectStyle,
            OutputType       = GetElement(doc, "OutputType") ?? "Library",
            AssemblyName     = GetElement(doc, "AssemblyName") ?? name,
            RootNamespace    = GetElement(doc, "RootNamespace") ?? name,
            TargetFrameworks = frameworks,
            FrameworkClass   = frameworks.Count > 0 ? ClassifyFramework(frameworks[0]) : "Unknown",
            LangVersion      = GetElement(doc, "LangVersion"),
            Nullable         = GetElement(doc, "Nullable"),
            ImplicitUsings   = GetElement(doc, "ImplicitUsings"),
            IsTestProject    = DetectTestProject(doc, packages),
            HasDockerfile    = Directory.GetFiles(dir, "Dockerfile*").Length > 0,
            ConfigFiles      = new ConfigFiles
            {
                AppSettings = Directory.GetFiles(dir, "appsettings*.json").Length > 0,
                WebConfig   = File.Exists(System.IO.Path.Combine(dir, "web.config"))
                           || File.Exists(System.IO.Path.Combine(dir, "Web.config")),
                AppConfig   = File.Exists(System.IO.Path.Combine(dir, "app.config"))
                           || File.Exists(System.IO.Path.Combine(dir, "App.config")),
            },
            CsFileCount  = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Length,
            Packages     = packages,
            ProjectRefs  = projectRefs,   // raw paths — resolved in second pass
            MigrationRisk = risk,
        };
    }

    // ── XML helpers ────────────────────────────────────────────────────

    /// Returns the inner text of the first matching element, ignoring XML namespaces.
    private static string? GetElement(XmlDocument doc, string localName)
    {
        var node = doc.SelectSingleNode($"//*[local-name()='{localName}']");
        return node?.InnerText is { Length: > 0 } t ? t : null;
    }

    private static List<string> GetTargetFrameworks(XmlDocument doc)
    {
        var single = GetElement(doc, "TargetFramework");
        if (single is not null) return [single];

        var multi = GetElement(doc, "TargetFrameworks");
        if (multi is not null)
            return [.. multi.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        // Legacy csproj: <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
        var legacy = GetElement(doc, "TargetFrameworkVersion");
        if (legacy is not null)
        {
            // v4.8 -> net48, v4.5.2 -> net452, etc.
            var normalized = "net" + legacy.TrimStart('v').Replace(".", "");
            return [normalized];
        }

        return [];
    }

    private static List<PackageRef> GetPackageReferences(XmlDocument doc)
    {
        var refs = new List<PackageRef>();
        var nodes = doc.SelectNodes("//*[local-name()='PackageReference']");
        if (nodes is null) return refs;

        foreach (XmlElement node in nodes)
        {
            var pkgName = node.GetAttribute("Include");
            if (string.IsNullOrEmpty(pkgName))
                pkgName = node.GetAttribute("Update");
            if (string.IsNullOrEmpty(pkgName))
                continue;

            // Version can be an attribute OR a child element
            var version = node.GetAttribute("Version");
            if (string.IsNullOrEmpty(version))
            {
                var versionNode = node.SelectSingleNode("*[local-name()='Version']");
                version = versionNode?.InnerText ?? "";
            }

            refs.Add(new PackageRef { Name = pkgName, Version = version });
        }

        return refs;
    }

    private static List<string> GetProjectReferences(XmlDocument doc, string projectDir)
    {
        var refs = new List<string>();
        var nodes = doc.SelectNodes("//*[local-name()='ProjectReference']");
        if (nodes is null) return refs;

        foreach (XmlElement node in nodes)
        {
            var include = node.GetAttribute("Include");
            if (string.IsNullOrEmpty(include)) continue;

            // Normalize path separators and resolve relative path
            var normalized = include.Replace('\\', System.IO.Path.DirectorySeparatorChar)
                                    .Replace('/', System.IO.Path.DirectorySeparatorChar);
            var abs = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, normalized));
            refs.Add(abs);
        }

        return refs;
    }

    private static string GetProjectStyle(XmlDocument doc)
    {
        var project = doc.DocumentElement;
        if (project is null) return "Unknown";

        var sdk = project.GetAttribute("Sdk");
        if (!string.IsNullOrEmpty(sdk)) return "SDK-style";

        var toolsVersion = project.GetAttribute("ToolsVersion");
        if (!string.IsNullOrEmpty(toolsVersion)) return $"Legacy (ToolsVersion {toolsVersion})";

        return "Unknown";
    }

    private static bool DetectTestProject(XmlDocument doc, List<PackageRef> packages)
    {
        var isTestProp = GetElement(doc, "IsTestProject");
        if (isTestProp?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        var sdk = doc.DocumentElement?.GetAttribute("Sdk") ?? "";
        if (sdk.Contains("Test", StringComparison.OrdinalIgnoreCase))
            return true;

        return packages.Any(p =>
            p.Name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
            p.Name.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase) ||
            p.Name.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
    }

    // ── Risk assessment ────────────────────────────────────────────────

    private static MigrationRisk GetMigrationRisk(
        List<string> frameworks,
        List<PackageRef> packages,
        string projectStyle)
    {
        var level  = "Low";
        var issues = new List<string>();

        foreach (var tfm in frameworks)
        {
            if (IsLegacyFramework(tfm))
            {
                level = "High";
                issues.Add($"Targets .NET Framework ({tfm})");
            }
            else if (IsOldNetStandard(tfm))
            {
                if (level != "High") level = "Medium";
                issues.Add($"Targets old netstandard ({tfm})");
            }
        }

        if (projectStyle.StartsWith("Legacy"))
        {
            level = "High";
            issues.Add("Legacy (non-SDK) project format");
        }

        foreach (var pkg in packages)
        {
            foreach (var (pattern, note, isHigh) in RiskyPackages)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(pkg.Name, pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    continue;

                if (isHigh)
                    level = "High";
                else if (level == "Low")
                    level = "Medium";

                issues.Add($"{note} [{pkg.Name}]");
                break; // one note per package
            }
        }

        return new MigrationRisk { Level = level, Issues = issues };
    }

    private static bool IsLegacyFramework(string tfm)
    {
        if (tfm.StartsWith("netframework", StringComparison.OrdinalIgnoreCase)) return true;
        // net1, net2, net3, net4 (but NOT net5+, net6, net7, net8...)
        if (System.Text.RegularExpressions.Regex.IsMatch(tfm, @"^net[1-4]\d*$")) return true;
        // net45, net451, net452, net46, net461... net48, net481
        if (System.Text.RegularExpressions.Regex.IsMatch(tfm, @"^net\d{2,3}$"))
        {
            var num = int.Parse(System.Text.RegularExpressions.Regex.Match(tfm, @"\d+").Value);
            return num < 500;
        }
        return false;
    }

    private static bool IsOldNetStandard(string tfm) =>
        System.Text.RegularExpressions.Regex.IsMatch(tfm, @"^netstandard1\.[0-4]$");

    private static string ClassifyFramework(string tfm)
    {
        if (IsLegacyFramework(tfm)) return ".NET Framework";
        if (tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)) return ".NET Standard";
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)) return ".NET Core";
        if (System.Text.RegularExpressions.Regex.IsMatch(tfm, @"^net[5-9]|^net\d{2,}\."))
            return ".NET (modern)";
        return "Other";
    }
}
