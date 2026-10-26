using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed class MobileNavigationState
{
    public BridgePageInfo[] Pages { get; private set; } = [];
    public string? PluginId { get; private set; }
    public string? PageId { get; private set; }
    public BridgePageInfo? Current => Pages.FirstOrDefault(p => p.PluginId == PluginId && p.PageId == PageId);

    public void Replace(IEnumerable<BridgePageInfo> pages)
        => Pages = pages.DistinctBy(p => (p.PluginId, p.PageId)).ToArray();

    public void Select(string pluginId, string pageId)
    {
        if (!Pages.Any(p => p.PluginId == pluginId && p.PageId == pageId)) throw new ArgumentException("Page is not available.");
        PluginId = pluginId; PageId = pageId;
    }

    public BridgePageInfo[] Shortcuts()
    {
        var first = Pages.Take(3).ToArray();
        return Current is { } selected && !first.Contains(selected) ? first.Take(2).Append(selected).ToArray() : first;
    }

    public void Clear() { Pages = []; PluginId = null; PageId = null; }
}
