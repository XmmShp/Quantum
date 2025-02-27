using Quantum.Infrastructure.Models;
using Quantum.Sdk;
using Quantum.Sdk.Services;

namespace Quantum.Runtime.Services;

public class Quantum(IModuleManager moduleManager, IInjectedCodeManager injectedCodeManager) : IQuantum
{
    public IModuleManager ModuleManager => moduleManager;
    public IInjectedCodeManager InjectedCodeManager => injectedCodeManager;
}
