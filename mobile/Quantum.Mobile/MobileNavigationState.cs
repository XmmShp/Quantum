using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed class MobileNavigationState
{
    private List<string> _order = [];
    public string[] Order => _order.ToArray();
    public static string Key(BridgePageInfo page) => page.PluginId + "/" + page.PageId;
    public BridgePageInfo[] Pages { get; private set; } = [];
    public string? PluginId { get; private set; }
    public string? PageId { get; private set; }
    public BridgePageInfo? Current => Pages.FirstOrDefault(p => p.PluginId == PluginId && p.PageId == PageId);

    public void Replace(IEnumerable<BridgePageInfo> pages)
    {
        var distinct = pages.DistinctBy(p => (p.PluginId, p.PageId)).ToArray();
        foreach (var page in distinct)
            if (!_order.Contains(Key(page))) _order.Add(Key(page));
        Pages = distinct.OrderBy(p => _order.IndexOf(Key(p))).GroupBy(p => p.PluginId).SelectMany(group => group).ToArray();
    }

    public void RestoreOrder(IEnumerable<string> order)
    {
        _order = order.Where(key => !string.IsNullOrWhiteSpace(key) && key.Length <= 512).Distinct(StringComparer.Ordinal).Take(256).ToList();
        Replace(Pages);
    }

    public void Move(string source, string target)
    {
        var from = Pages.FirstOrDefault(p => Key(p) == source) ?? throw new ArgumentException("Page is not available.");
        var to = Pages.FirstOrDefault(p => Key(p) == target) ?? throw new ArgumentException("Page is not available.");
        var groups = Pages.GroupBy(p => p.PluginId).Select(group => group.ToList()).ToList();
        var fromIndex = groups.FindIndex(group => group[0].PluginId == from.PluginId);
        var toIndex = groups.FindIndex(group => group[0].PluginId == to.PluginId);
        if (fromIndex == toIndex)
        {
            var targetIndex = groups[fromIndex].IndexOf(to);
            groups[fromIndex].Remove(from);
            groups[fromIndex].Insert(targetIndex, from);
        }
        else
        {
            var moving = groups[fromIndex]; groups.RemoveAt(fromIndex); groups.Insert(toIndex, moving);
        }
        var keys = groups.SelectMany(group => group).Select(Key).ToList();
        _order = keys.Concat(_order.Except(keys, StringComparer.Ordinal)).Take(256).ToList();
        Replace(Pages);
    }

    public void Select(string pluginId, string pageId)
    {
        if (!Pages.Any(p => p.PluginId == pluginId && p.PageId == pageId)) throw new ArgumentException("Page is not available.");
        PluginId = pluginId; PageId = pageId;
    }

    public BridgePageInfo[] Shortcuts()
    {
        return Pages.GroupBy(p => p.PluginId).Take(4).Select(group => group.FirstOrDefault(p => p.PageId == PageId && p.PluginId == PluginId) ?? group.First()).ToArray();
    }

    public void Clear() { Pages = []; PluginId = null; PageId = null; _order.Clear(); }
}
