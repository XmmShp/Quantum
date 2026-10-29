using Quantum.Mobile.Internal;
using Quantum.MobileApp;

namespace Quantum.Tests;

public sealed class MobileDesktopSelectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-27T06:00:00Z");
    private static BridgeDevice Desktop(string id, string name, bool online = true)
        => new(id, name, "desktop", online ? Now : Now.AddMinutes(-2), "session");

    [Fact]
    public void FirstLoginChoosesTheFirstOnlineComputerInMenuOrder()
    {
        var devices = new[] { Desktop("b", "Work"), Desktop("z", "Archive", false), Desktop("a", "Home"), new BridgeDevice("phone", "A phone", "mobile", Now, "session") };
        Assert.Equal("a", MobileDesktopSelection.Choose(devices, null, Now)?.Id);
    }

    [Fact]
    public void SubsequentLoginRestoresTheRememberedDesktop()
    {
        var devices = new[] { Desktop("a", "Home"), Desktop("b", "Work") };
        Assert.Equal("b", MobileDesktopSelection.Choose(devices, "b", Now)?.Id);
    }

    [Fact]
    public void OfflineOrExpiredRememberedDesktopDoesNotSilentlySwitchToAnother()
    {
        var devices = new[] { Desktop("a", "Home"), Desktop("b", "Work", false) };
        var offline = MobileDesktopSelection.Choose(devices, "b", Now);
        Assert.Equal("b", offline?.Id);
        Assert.False(MobileDesktopSelection.IsOnline(offline!, Now));
        Assert.Null(MobileDesktopSelection.Choose(devices, "expired-id", Now));
    }

    [Fact]
    public void NoOnlineDesktopsWaitsForOneToBecomeAvailable()
    {
        Assert.Null(MobileDesktopSelection.Choose([Desktop("a", "Home", false)], null, Now));
        Assert.Equal("a", MobileDesktopSelection.Choose([Desktop("a", "Home")], null, Now)?.Id);
    }

    [Fact]
    public void RotatedRegistrationRecoversByUniqueNameWithinTheAccount()
    {
        Assert.Equal("new-id", MobileDesktopSelection.Choose([Desktop("other", "Home"), Desktop("new-id", "Work")], "old-id", Now, "Work")?.Id);
        Assert.Null(MobileDesktopSelection.Choose([Desktop("one", "Work"), Desktop("two", "Work")], "old-id", Now, "Work"));
        Assert.Null(MobileDesktopSelection.Choose([Desktop("one", "Work", false)], "old-id", Now, "Work"));
    }
}
