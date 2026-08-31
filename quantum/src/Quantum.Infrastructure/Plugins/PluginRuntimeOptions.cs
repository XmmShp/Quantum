namespace Quantum.Infrastructure.Plugins;

public sealed record PluginRuntimeOptions(string ModulesRootPath, string ShadowRootPath)
{
    public PluginRuntimeOptions Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ModulesRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ShadowRootPath);
        return this;
    }
}
