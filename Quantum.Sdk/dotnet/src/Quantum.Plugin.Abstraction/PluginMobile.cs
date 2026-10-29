
namespace Quantum.Plugin.Abstraction;

/// <summary>Publishes immutable data for a desktop runtime's mobile pages. No code is sent to phones.</summary>
public interface IQuantumMobilePages
{
    void Publish(MobilePage page);
    void Remove(string pageId);
}
