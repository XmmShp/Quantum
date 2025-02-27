using Quantum.Infrastructure.Abstractions;
using Quantum.Sdk.Services;

namespace Quantum.Runtime.Services;

internal class ServiceManager(IServiceCollection services) : IServiceManager
{
    public IServiceManager AddEagerInitializeService(IInitializableService service)
    {
        services.AddSingleton(service);
        return this;
    }
}
