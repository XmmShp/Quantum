namespace Quantum.Plugin.Abstraction;

public interface IQuantumPluginRuntimeContext
{
    QuantumPluginInfo Plugin { get; }

    string RootPath { get; }
}
