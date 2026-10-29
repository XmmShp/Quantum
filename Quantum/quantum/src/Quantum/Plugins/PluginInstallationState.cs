namespace Quantum.Plugins;

public enum PluginInstallationState
{
    Unknown,
    Staged,
    Installed,
    UpdatePending,
    UninstallPending,
    Failed
}
