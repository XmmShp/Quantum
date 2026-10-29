using Quantum.Mobile.Internal;
using Quantum.MobileApp;

namespace Quantum.Tests;

public sealed class MobileNavigationStateTests
{
    [Fact]
    public void FourPinnedPagesDoNotChangeWhenAnotherPluginIsOpened()
    {
        var state = new MobileNavigationState();
        var pages = Enumerable.Range(0, 8).Select(i => new BridgePageInfo("plugin." + i, "home", "Plugin " + i)).ToArray();
        state.Replace(pages);
        state.Select(pages[7].PluginId, pages[7].PageId);
        Assert.Equal(pages[7], state.Current);
        Assert.Equal(pages.Take(4).ToArray(), state.Shortcuts());
    }

    [Fact]
    public void DragOrderRestoresAcrossRestartAndCatalogChangesWithoutDiscardingCurrentPage()
    {
        var pages = Enumerable.Range(0, 6).Select(i => new BridgePageInfo("plugin." + i, "home", "Plugin " + i)).ToArray();
        var state = new MobileNavigationState();
        state.Replace(pages); state.Select(pages[1].PluginId, pages[1].PageId);
        state.Move(MobileNavigationState.Key(pages[5]), MobileNavigationState.Key(pages[0]));
        Assert.Equal(pages[5], state.Shortcuts()[0]);
        Assert.Equal(pages[1], state.Current);
        var restored = new MobileNavigationState();
        restored.RestoreOrder(state.Order);
        restored.Replace(pages.Where(p => p != pages[5]));
        restored.Replace(pages);
        Assert.Equal(pages[5], restored.Shortcuts()[0]);
        Assert.Equal(4, restored.Shortcuts().Length);
        Assert.Throws<ArgumentException>(() => restored.Move("missing", MobileNavigationState.Key(pages[0])));
    }

    [Fact]
    public void MultiplePagesOfOnePluginUseOnlyOnePinnedIconAndMoveTogether()
    {
        var first = new BridgePageInfo("calendar", "today", "Today");
        var second = new BridgePageInfo("calendar", "tasks", "Tasks");
        var ledger = new BridgePageInfo("ledger", "home", "Ledger");
        var state = new MobileNavigationState();
        state.Replace([first, second, ledger]); state.Select(second.PluginId, second.PageId);
        Assert.Equal(new[] { second, ledger }, state.Shortcuts());
        state.Move(MobileNavigationState.Key(second), MobileNavigationState.Key(ledger));
        Assert.Equal(new[] { ledger, first, second }, state.Pages);
        Assert.Equal(second, state.Current);
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
