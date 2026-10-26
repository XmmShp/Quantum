using Quantum.Mobile.Internal;
using Quantum.MobileApp;

namespace Quantum.Tests;

public sealed class MobileNavigationStateTests
{
    [Fact]
    public void ActivePageRemainsVisibleInShortcutsWhenThereAreManyPlugins()
    {
        var state = new MobileNavigationState();
        var pages = Enumerable.Range(0, 8).Select(i => new BridgePageInfo("plugin." + i, "home", "Plugin " + i)).ToArray();
        state.Replace(pages);
        state.Select(pages[7].PluginId, pages[7].PageId);
        Assert.Equal(pages[7], state.Current);
        Assert.Equal(new[] { pages[0], pages[1], pages[7] }, state.Shortcuts());
    }

    [Fact]
    public void CatalogRefreshDeduplicatesAndDoesNotSelectADifferentDraftImplicitly()
    {
        var state = new MobileNavigationState();
        var first = new BridgePageInfo("calendar", "home", "Calendar");
        var second = new BridgePageInfo("ledger", "home", "Ledger");
        state.Replace([first, second, second]); state.Select(first.PluginId, first.PageId);
        Assert.Equal(2, state.Pages.Length);
        state.Replace([second]);
        Assert.Null(state.Current);
        Assert.Equal("calendar", state.PluginId);
        Assert.Throws<ArgumentException>(() => state.Select("missing", "home"));
        state.Clear();
        Assert.Empty(state.Shortcuts()); Assert.Null(state.PluginId);
    }
}
