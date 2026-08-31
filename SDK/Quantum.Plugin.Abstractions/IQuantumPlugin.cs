namespace Quantum.Plugin.Abstractions;

public interface IQuantumPlugin
{
    Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}
