using System.Reflection;

namespace Quantum.Plugins;

public interface IPluginRuntimeManager
{
    bool IsInitialized { get; }

    Task InitializeAsync(IServiceProvider hostServices, CancellationToken cancellationToken = default);

    Task<PluginOperationResult> ReloadAsync(string pluginId, CancellationToken cancellationToken = default);

    PluginUnloadImpact GetUnloadImpact(string pluginId);

    Task<PluginOperationResult> UnloadAsync(string pluginId, CancellationToken cancellationToken = default);

    Task<PluginOperationResult> UnloadAsync(
        string pluginId,
        long confirmedCatalogRevision,
        CancellationToken cancellationToken = default);

    Task<PluginOperationResult> RefreshAsync(CancellationToken cancellationToken = default);

    Task<PluginInstallPreview> PrepareInstallAsync(
        Stream archiveStream,
        string archiveFileName,
        CancellationToken cancellationToken = default);

    Task<PluginOperationResult> InstallAsync(
        string previewId,
        CancellationToken cancellationToken = default);

    Task CancelInstallAsync(
        string previewId,
        CancellationToken cancellationToken = default);

    IServiceProvider? GetPluginServices(Assembly assembly);
}

public sealed record PluginUnloadImpact(
    string PluginId,
    IReadOnlyList<string> DependentPluginIds,
    long CatalogRevision);

public sealed record PluginOperationResult(bool Succeeded, string Message)
{
    public static PluginOperationResult Success(string message) => new(true, message);

    public static PluginOperationResult Failure(string message) => new(false, message);
}

public enum PluginInstallAction
{
    Install,
    Upgrade,
    KeepInstalled
}

public sealed record PluginInstallPreviewItem(
    string PluginId,
    string PackageVersion,
    string? InstalledVersion,
    PluginInstallAction Action);

public sealed record PluginInstallIssue(string? PluginId, string Reason);

public sealed record PluginInstallPreview(
    string? PreviewId,
    string ArchiveFileName,
    IReadOnlyList<PluginInstallPreviewItem> Plugins,
    IReadOnlyList<PluginInstallIssue> Issues)
{
    public bool CanInstall => PreviewId is not null && Issues.Count == 0
        && Plugins.Any(static plugin => plugin.Action is PluginInstallAction.Install or PluginInstallAction.Upgrade);

    public int InstallCount => Plugins.Count(static plugin =>
        plugin.Action is PluginInstallAction.Install or PluginInstallAction.Upgrade);
}

public interface IPluginReferenceRelease
{
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
