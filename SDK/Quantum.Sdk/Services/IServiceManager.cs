using Quantum.Infrastructure.Abstractions;

namespace Quantum.Sdk.Services;

public interface IServiceManager
{
    IServiceManager AddEagerInitializeService(IInitializableService service);
}
