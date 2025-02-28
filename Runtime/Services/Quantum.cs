using ElectronNET.API;
using Quantum.Sdk;
using Quantum.Sdk.Services;

namespace Quantum.Runtime.Services;

internal class Quantum : IQuantum
{
    public BrowserWindow? Window { get; set; }
    public required IServiceCollection HostServices { get; init; }
    public required IModuleManager ModuleManager { get; init; }
    public required IInjectedCodeManager InjectedCodeManager { get; init; }
}
