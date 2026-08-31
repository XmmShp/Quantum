namespace Quantum.Plugin.Abstraction;

public interface IQuantumPlugin
{
    Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}
