namespace Quantum.Plugin.Abstraction;

public interface IQuantumPluginEnvironment
{
    IReadOnlyList<QuantumPluginInfo> LoadedPlugins { get; }

    bool IsPluginLoaded(string pluginId);

    bool IsIntegrationActive(string ownerPluginId, string targetPluginId);
}

public sealed record QuantumPluginInfo(string Id, string Version);
