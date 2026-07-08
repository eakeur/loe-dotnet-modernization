namespace DotNetModAssess.Core.Models;

public sealed class LegacyCouplingSignals
{
    public bool ReferencesSystemWeb { get; init; }
    public bool ReferencesSystemServiceModel { get; init; }
    public bool ReferencesSystemMessaging { get; init; }
    public IReadOnlyList<string> ComReferences { get; init; } = [];
    public bool HasPInvokeSignals { get; init; }
    public bool HasAppConfig { get; init; }
    public bool HasWebConfig { get; init; }
    public bool UsesConfigurationManager { get; init; }
    public IReadOnlyList<string> LegacyAssemblyReferences { get; init; } = [];
    public bool HasWebConfigTransforms { get; init; }
    public IReadOnlyList<string> WindowsOnlyAssemblyReferences { get; init; } = [];
}
