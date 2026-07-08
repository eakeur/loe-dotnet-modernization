namespace DotNetModAssess.Core.Fixtures;

public sealed record FixtureLegacyCouplingSignalsDto(
    bool ReferencesSystemWeb,
    bool ReferencesSystemServiceModel,
    bool ReferencesSystemMessaging,
    IReadOnlyList<string> ComReferences,
    bool HasPInvokeSignals,
    bool HasAppConfig,
    bool HasWebConfig,
    bool UsesConfigurationManager,
    IReadOnlyList<string> LegacyAssemblyReferences,
    bool HasWebConfigTransforms,
    IReadOnlyList<string> WindowsOnlyAssemblyReferences);
