using System.Text.RegularExpressions;
using DotNetModAssess.Core.Models;

namespace DotNetModAssess.Core.Parsing.Internal;

internal sealed record ProjectBuildData(
    ProjectFormat Format,
    IReadOnlyList<string> TargetFrameworks,
    ProjectOutputType OutputType,
    IReadOnlyList<PackageReferenceModel> PackageReferences,
    ProjectMetadata Metadata);

/// <summary>
/// Combines the evaluation-independent raw XML view of a project (<see cref="RawProjectFile"/>)
/// with whatever MSBuild evaluation data was obtainable (<see cref="ProjectEvaluationOutcome"/>)
/// and central-package-management/Directory.Build context into the full <see cref="ProjectMetadata"/>
/// shape. Evaluated data is preferred wherever available since it reflects what MSBuild actually
/// resolved; raw-XML data is the fallback (and, for format/legacy-signal detection, the primary
/// source regardless of evaluation outcome, per the parsing engine's design contract).
/// </summary>
internal static partial class ProjectMetadataBuilder
{
    private static readonly HashSet<string> TestFrameworkPackageMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "xunit.core",
        "xunit.runner.visualstudio",
        "NUnit",
        "NUnit3TestAdapter",
        "MSTest.TestFramework",
        "MSTest.TestAdapter",
    };

    public static ProjectBuildData Build(
        RawProjectFile rawFile,
        ProjectEvaluationOutcome evaluation,
        bool isCentrallyManaged,
        IReadOnlyDictionary<string, string> cpmPackageVersions,
        IReadOnlyList<string> directoryBuildPropsChain,
        IReadOnlyList<string> directoryBuildTargetsChain,
        IReadOnlyList<Guid> projectTypeGuids,
        IReadOnlyList<EvaluationDiagnostic> extraDiagnostics)
    {
        var properties = evaluation.Result?.Properties ?? new Dictionary<string, string>();
        var referencePaths = evaluation.Result?.References ?? [];

        var targetFrameworksRaw = rawFile.TargetFrameworksRaw.Count > 0
            ? rawFile.TargetFrameworksRaw
            : (properties.TryGetValue("TargetFramework", out var evaluatedTfm) && !string.IsNullOrWhiteSpace(evaluatedTfm)
                ? [evaluatedTfm]
                : []);

        var normalizedFrameworks = targetFrameworksRaw.Select(TargetFrameworkClassifier.NormalizeToMoniker).ToList();
        var tfmRawJoined = string.Join(';', targetFrameworksRaw);
        var classification = TargetFrameworkClassifier.Classify(targetFrameworksRaw);

        var outputType = ResolveOutputType(properties, rawFile);

        var packagesModel = rawFile.HasPackagesConfig
            ? PackagesModel.PackagesConfig
            : rawFile.PackageReferences.Count > 0
                ? PackagesModel.PackageReference
                : PackagesModel.None;

        var packageReferences = BuildPackageReferences(rawFile, isCentrallyManaged, cpmPackageVersions, referencePaths);

        var isTestProject = DeterminePackagesIndicateTest(packageReferences)
            || (properties.TryGetValue("IsTestProject", out var isTestProp) && bool.TryParse(isTestProp, out var isTestValue) && isTestValue);
        var testFramework = DetermineTestFramework(packageReferences);

        var diagnostics = new List<EvaluationDiagnostic>(extraDiagnostics);
        diagnostics.AddRange(ExtractBuildDiagnostics(evaluation));

        var legacySignals = new LegacyCouplingSignals
        {
            ReferencesSystemWeb = rawFile.ReferencesSystemWeb,
            ReferencesSystemServiceModel = rawFile.ReferencesSystemServiceModel,
            ReferencesSystemMessaging = rawFile.ReferencesSystemMessaging,
            ComReferences = rawFile.ComReferences,
            HasPInvokeSignals = ScanSourceForMarker(rawFile, evaluation, "DllImport"),
            HasAppConfig = rawFile.HasAppConfig,
            HasWebConfig = rawFile.HasWebConfig,
            UsesConfigurationManager = ScanSourceForMarker(rawFile, evaluation, "ConfigurationManager"),
            LegacyAssemblyReferences = rawFile.LegacyAssemblyReferences,
            HasWebConfigTransforms = rawFile.HasWebConfigTransforms,
            WindowsOnlyAssemblyReferences = rawFile.WindowsOnlyAssemblyReferences,
        };

        var customTargetsAndImports = new List<CustomBuildElement>(rawFile.CustomTargetsAndImports);
        foreach (var chainFile in directoryBuildPropsChain.Concat(directoryBuildTargetsChain))
        {
            customTargetsAndImports.AddRange(RawProjectFile.Load(chainFile).CustomTargetsAndImports);
        }

        var metadata = new ProjectMetadata
        {
            RawProperties = properties,
            Format = rawFile.Format,
            PackagesModel = packagesModel,
            IsCentrallyManaged = isCentrallyManaged,
            TargetFrameworkRaw = tfmRawJoined,
            TargetFrameworkClassification = classification,
            OutputType = outputType,
            ProjectTypeGuids = projectTypeGuids,
            LangVersion = properties.GetValueOrDefault("LangVersion"),
            Nullable = properties.GetValueOrDefault("Nullable"),
            ImplicitUsings = ParseBool(properties.GetValueOrDefault("ImplicitUsings")),
            AllowUnsafeBlocks = ParseBool(properties.GetValueOrDefault("AllowUnsafeBlocks")),
            TreatWarningsAsErrors = ParseBool(properties.GetValueOrDefault("TreatWarningsAsErrors")),
            DefineConstants = SplitSemicolons(properties.GetValueOrDefault("DefineConstants")),
            AssemblyName = properties.GetValueOrDefault("AssemblyName") ?? rawFile.AssemblyNameRaw,
            RootNamespace = properties.GetValueOrDefault("RootNamespace") ?? rawFile.RootNamespaceRaw,
            RuntimeIdentifiers = SplitSemicolons(properties.GetValueOrDefault("RuntimeIdentifiers") ?? properties.GetValueOrDefault("RuntimeIdentifier")),
            PlatformTarget = properties.GetValueOrDefault("PlatformTarget"),
            UseWindowsForms = ParseBool(properties.GetValueOrDefault("UseWindowsForms")) ?? false,
            UseWpf = ParseBool(properties.GetValueOrDefault("UseWPF")) ?? false,
            IsPackable = ParseBool(properties.GetValueOrDefault("IsPackable")),
            LegacySignals = legacySignals,
            DirectoryBuildPropsChain = directoryBuildPropsChain,
            DirectoryBuildTargetsChain = directoryBuildTargetsChain,
            CustomTargetsAndImports = customTargetsAndImports,
            IsTestProject = isTestProject,
            TestFramework = testFramework,
            Diagnostics = diagnostics,
        };

        return new ProjectBuildData(rawFile.Format, normalizedFrameworks, outputType, packageReferences, metadata);
    }

    private static ProjectOutputType ResolveOutputType(IReadOnlyDictionary<string, string> properties, RawProjectFile rawFile)
    {
        var raw = properties.GetValueOrDefault("OutputType") ?? rawFile.OutputTypeRaw;
        return raw?.Trim().ToLowerInvariant() switch
        {
            "exe" => ProjectOutputType.Exe,
            "winexe" => ProjectOutputType.WinExe,
            "appcontainerexe" => ProjectOutputType.AppContainerExe,
            "library" => ProjectOutputType.Library,
            null or "" => ProjectOutputType.Library, // MSBuild default when unspecified
            _ => ProjectOutputType.Unknown,
        };
    }

    private static List<PackageReferenceModel> BuildPackageReferences(
        RawProjectFile rawFile,
        bool isCentrallyManaged,
        IReadOnlyDictionary<string, string> cpmPackageVersions,
        string[] referencePaths)
    {
        var result = new List<PackageReferenceModel>();

        if (rawFile.HasPackagesConfig)
        {
            var packagesConfigPath = Path.Combine(Path.GetDirectoryName(rawFile.Path)!, "packages.config");
            foreach (var entry in PackagesConfigReader.Read(packagesConfigPath))
            {
                result.Add(new PackageReferenceModel
                {
                    PackageId = entry.PackageId,
                    RequestedVersion = entry.Version,
                    ResolvedVersion = entry.Version,
                    IsCentrallyManaged = false,
                    IsFloatingVersion = false,
                });
            }

            return result;
        }

        foreach (var raw in rawFile.PackageReferences)
        {
            var isFloating = raw.Version?.Contains('*') ?? false;
            var isBareReferenceUnderCpm = isCentrallyManaged && string.IsNullOrWhiteSpace(raw.Version);

            string? resolvedVersion = null;
            if (isBareReferenceUnderCpm && cpmPackageVersions.TryGetValue(raw.PackageId, out var cpmVersion))
            {
                resolvedVersion = cpmVersion;
            }
            else if (!string.IsNullOrWhiteSpace(raw.Version) && !isFloating)
            {
                resolvedVersion = raw.Version;
            }

            // Best-effort refinement: if we still don't have (or want to confirm) a resolved
            // version, look for the package's actual restored assembly path, which encodes the
            // exact resolved version (helps with floating versions and confirms CPM resolution).
            var fromReferencePath = TryResolveVersionFromReferencePaths(referencePaths, raw.PackageId);
            resolvedVersion = fromReferencePath ?? resolvedVersion;

            result.Add(new PackageReferenceModel
            {
                PackageId = raw.PackageId,
                RequestedVersion = raw.Version,
                ResolvedVersion = resolvedVersion,
                IsCentrallyManaged = isBareReferenceUnderCpm,
                IsFloatingVersion = isFloating,
            });
        }

        return result;
    }

    private static string? TryResolveVersionFromReferencePaths(string[] referencePaths, string packageId)
    {
        var idLower = packageId.ToLowerInvariant();
        foreach (var path in referencePaths)
        {
            var pathLower = path.ToLowerInvariant().Replace('\\', '/');
            var marker = "/" + idLower + "/";
            var index = pathLower.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) continue;

            var remainder = path[(index + marker.Length)..];
            var match = Regex.Match(remainder, @"^(?<version>\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z\.\-]+)?)[/\\]");
            if (match.Success)
            {
                return match.Groups["version"].Value;
            }
        }

        return null;
    }

    private static bool DeterminePackagesIndicateTest(IEnumerable<PackageReferenceModel> packages) =>
        packages.Any(p => TestFrameworkPackageMarkers.Contains(p.PackageId));

    private static string? DetermineTestFramework(IEnumerable<PackageReferenceModel> packages)
    {
        var ids = packages.Select(p => p.PackageId).ToList();
        if (ids.Any(id => id.StartsWith("xunit", StringComparison.OrdinalIgnoreCase))) return "xUnit";
        if (ids.Any(id => id.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase))) return "NUnit";
        if (ids.Any(id => id.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase))) return "MSTest";
        return null;
    }

    private static bool? ParseBool(string? value) =>
        !string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var result) ? result : null;

    private static List<string> SplitSemicolons(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<EvaluationDiagnostic> ExtractBuildDiagnostics(ProjectEvaluationOutcome evaluation)
    {
        var diagnostics = new List<EvaluationDiagnostic>();
        foreach (var arg in evaluation.BuildEvents)
        {
            switch (arg)
            {
                case Microsoft.Build.Framework.BuildErrorEventArgs error:
                    diagnostics.Add(new EvaluationDiagnostic
                    {
                        Severity = EvaluationDiagnosticSeverity.Error,
                        Code = error.Code ?? "MSBUILD_ERROR",
                        Message = error.Message ?? string.Empty,
                        File = error.File,
                        LineNumber = error.LineNumber == 0 ? null : error.LineNumber,
                    });
                    break;
                case Microsoft.Build.Framework.BuildWarningEventArgs warning:
                    diagnostics.Add(new EvaluationDiagnostic
                    {
                        Severity = EvaluationDiagnosticSeverity.Warning,
                        Code = warning.Code ?? "MSBUILD_WARNING",
                        Message = warning.Message ?? string.Empty,
                        File = warning.File,
                        LineNumber = warning.LineNumber == 0 ? null : warning.LineNumber,
                    });
                    break;
            }
        }

        return diagnostics;
    }

    private static bool ScanSourceForMarker(RawProjectFile rawFile, ProjectEvaluationOutcome evaluation, string marker)
    {
        IEnumerable<string> sourceFiles = evaluation.Result?.SourceFiles is { Length: > 0 } evaluated
            ? evaluated
            : SafeEnumerateSourceFiles(Path.GetDirectoryName(rawFile.Path)!);

        foreach (var file in sourceFiles)
        {
            try
            {
                if (File.Exists(file) && File.ReadAllText(file).Contains(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // Best-effort scan; ignore unreadable files.
            }
        }

        return false;
    }

    private static IEnumerable<string> SafeEnumerateSourceFiles(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory)) yield break;

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }
}
