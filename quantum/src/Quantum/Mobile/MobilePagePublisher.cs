using MobilePage = Quantum.Plugin.Abstraction.MobilePage;
using MobileUiValidator = Quantum.Plugin.Abstraction.MobileUiValidator;
using System.Text.Json;
using Quantum.Mobile.Internal;

namespace Quantum.Mobile;

public sealed class MobilePagePublisher : IQuantumMobilePages
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MobilePage> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _content = new(StringComparer.Ordinal);
    public long Revision { get; private set; }
    public void Publish(MobilePage page)
    {
        MobileUiValidator.Validate(page);
        // Keep only SDK declaration types and JSON, never plugin-owned objects or delegates.
        var copy = MobileJson.Read<MobilePage>(MobileJson.Element(page));
        lock (_gate)
        {
            var content = JsonSerializer.Serialize(copy, MobileJson.Options);
            if (_content.GetValueOrDefault(page.Id) == content) return;
            if (!_pages.ContainsKey(page.Id) && _pages.Count >= 100) throw new ArgumentException("Mobile page quota reached.");
            _pages[page.Id] = copy;
            _content[page.Id] = content;
            Revision++;
        }
    }
    public void Remove(string pageId) { lock (_gate) { _pages.Remove(pageId); _content.Remove(pageId); Revision++; } }
    public MobilePage[] Snapshot() { lock (_gate) return _pages.Values.ToArray(); }
    public (MobilePage Page, long Revision) Get(string pageId)
    {
        lock (_gate) return (_pages.GetValueOrDefault(pageId) ?? throw new BridgeException("mobile_not_found", "Page is unavailable."), Revision);
    }
    public void Clear() { lock (_gate) { _pages.Clear(); _content.Clear(); Revision++; } }
}
