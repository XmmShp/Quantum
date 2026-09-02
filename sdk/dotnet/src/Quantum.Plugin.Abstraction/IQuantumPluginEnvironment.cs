namespace Quantum.Plugin.Abstraction;

public interface IQuantumPluginEnvironment
{
    IReadOnlyList<QuantumPluginInfo> LoadedPlugins { get; }

    bool IsPluginLoaded(PluginId pluginId);
}

public sealed record QuantumPluginInfo
{
    [System.Text.Json.Serialization.JsonConstructor]
    public QuantumPluginInfo(PluginId id, SemanticVersion version)
    {
        _ = (string)id;
        _ = (string)version;
        Id = id;
        Version = version;
    }

    public QuantumPluginInfo(string id, string version)
        : this(PluginId.Of(id), SemanticVersion.Of(version))
    {
    }

    public PluginId Id { get; }

    public SemanticVersion Version { get; }
}
