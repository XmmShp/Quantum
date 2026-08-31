using System.Reflection;

namespace Quantum.Application.Plugins;

public interface IPluginRuntimeManager
{
    bool IsInitialized { get; }

    Task InitializeAsync(IServiceProvider hostServices, CancellationToken cancellationToken = default);

    Task<PluginOperationResult> ReloadAsync(string pluginId, CancellationToken cancellationToken = default);

    Task<PluginOperationResult> UnloadAsync(string pluginId, CancellationToken cancellationToken = default);

    Task<PluginOperationResult> RefreshAsync(CancellationToken cancellationToken = default);

    IServiceProvider? GetPluginServices(Assembly assembly);
}

public sealed record PluginOperationResult(bool Succeeded, string Message)
{
    public static PluginOperationResult Success(string message) => new(true, message);

    public static PluginOperationResult Failure(string message) => new(false, message);
}

public interface IPluginReferenceRelease
{
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
