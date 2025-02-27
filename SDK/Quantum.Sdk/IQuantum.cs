using ElectronNET.API;
using Quantum.Infrastructure.Models;
using Quantum.Sdk.Services;

namespace Quantum.Sdk;
public interface IQuantum
{
    BrowserWindow? Window { get; }
    IServiceManager ServiceManager { get; }
    IModuleManager ModuleManager { get; }
    IInjectedCodeManager InjectedCodeManager { get; }
}
