using ElectronNET.API;
using Quantum.Infrastructure.Models;
using Quantum.Sdk;
using Quantum.Sdk.Services;

namespace Quantum.Runtime.Services;

internal class Quantum(ModuleManager moduleManager, InjectedCodeManager injectedCodeManager, ServiceManager serviceManager) : IQuantum
{
    public BrowserWindow? Window { get; set; }
    public IServiceManager ServiceManager => serviceManager;
    public IModuleManager ModuleManager => moduleManager;
    public IInjectedCodeManager InjectedCodeManager => injectedCodeManager;
}
