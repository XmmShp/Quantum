namespace Quantum.Plugin.Abstraction;

/// <summary>
/// Bootstraps and tears down one generation of a .NET plugin.
/// </summary>
/// <remarks>
/// Implementations are discovered from the plugin entry assembly and are never instantiated or
/// registered in dependency injection. Both methods receive the same plugin-runtime scoped
/// <see cref="IServiceProvider"/>. Durable or observable state belongs in regular plugin services,
/// and the runtime disposes the scope after the plugin stops.
/// </remarks>
public interface IQuantumPlugin
{
    /// <summary>
    /// Starts the plugin using its runtime-scoped service provider.
    /// </summary>
    static abstract Task StartAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the plugin using the same runtime-scoped service provider supplied to
    /// <see cref="StartAsync"/>.
    /// </summary>
    static abstract Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default);
}
