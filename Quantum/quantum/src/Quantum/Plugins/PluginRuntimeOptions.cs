namespace Quantum.Plugins;

public sealed record PluginRuntimeOptions(
    string ModulesRootPath,
    string ShadowRootPath,
    string DatabasePath)
{
    public PluginRuntimeOptions Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ModulesRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ShadowRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabasePath);
        return this;
    }
}
