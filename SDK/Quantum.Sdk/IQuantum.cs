using Quantum.Infrastructure.Models;
using Quantum.Sdk.Services;

namespace Quantum.Sdk;
public interface IQuantum
{
    IModuleManager ModuleManager { get; }
    IInjectedCodeManager InjectedCodeManager { get; }
}
