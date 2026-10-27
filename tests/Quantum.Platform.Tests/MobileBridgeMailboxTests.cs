using Quantum.Mobile.Internal;
using Quantum.Platform.Application;
using System.Text.Json;
using Xunit;
using MobileUi = Quantum.Mobile.Internal.Mobile;

namespace Quantum.Platform.Tests;

public sealed class MobileBridgeMailboxTests
{
    [Fact]
    public void MobileAndAccountServicesUseDistinctHttpRoutes()
    {
        static string Route(Type contract) => (string)contract.GetCustomAttributesData()
            .Single(attribute => attribute.AttributeType.Name == "TransportOverHttpAttribute").ConstructorArguments.Last().Value!;
        Assert.Equal("/rpc/mobile", Route(typeof(Quantum.Platform.Contract.IMobileBridgeService)));
        Assert.NotEqual(Route(typeof(Quantum.Platform.Contract.IQuantumPlatformService)), Route(typeof(Quantum.Platform.Contract.IMobileBridgeService)));
    }
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    private readonly MobileBridgeMailbox _mailbox = new();
    private JsonElement Call(BridgeRegistration device, string operation, object? body = null, DateTimeOffset? now = null)
        => _mailbox.Exchange(new(operation, device.Device.Id, device.Secret, MobileJson.Element(body ?? new { })), now ?? Now);
    private BridgeRegistration Register(string kind) => MobileJson.Read<BridgeRegistration>(_mailbox.Exchange(new("register", Body: MobileJson.Element(new { name = kind, kind })), Now));
    private void PublishCatalog(BridgeRegistration desktop, BridgeRegistration phone)
    {
        Call(desktop, "catalog", new { pages = new[] { new BridgePageInfo("plugin.notes", "notes", "Notes") } });
    }
    private BridgeSnapshot Publish(BridgeRegistration desktop, BridgeRegistration phone)
    {
        var page = MobileUi.Page("notes", "Notes", new MobileNode("Button", "save") { Text = "Save", ActionId = "fixed" });
        var snapshot = new BridgeSnapshot("view", desktop.Device.SessionId, "plugin.notes", "runtime", Now.AddMinutes(5), page);
        Call(desktop, "send", new BridgeSend("snapshot", phone.Device.Id, "snapshot", MobileJson.Element(snapshot)));
        return snapshot;
    }
    [Fact]
    public void AccessRequiresDeviceSecretAndMatchingAccountAndDeviceType()
    {
        var desktop = Register("desktop"); var phone = Register("mobile");
        Assert.Empty(MobileJson.Read<BridgePageInfo[]>(Call(phone, "pages", new { desktopId = desktop.Device.Id })));
        Assert.Throws<BridgeException>(() => Call(phone with { Secret = "forged" }, "poll"));
        Assert.Throws<BridgeException>(() => Call(phone, "catalog", new { pages = Array.Empty<BridgePageInfo>() }));
        // A device from another account is absent from this account-scoped aggregate.
        var other = new MobileBridgeMailbox();
        Assert.Throws<BridgeException>(() => other.Exchange(new("poll", phone.Device.Id, phone.Secret, MobileJson.Element(new { })), Now));
    }
    [Fact]
    public void SameAccountCanReadPagesWithoutAnyBindOrApprovalAndLegacyFlagsCannotBlockIt()
    {
        var desktop = Register("desktop"); var phone = Register("mobile");
        PublishCatalog(desktop, phone);
        _mailbox.Bindings.Add(new(desktop.Device.Id, phone.Device.Id, "legacy", false));
        Assert.Single(MobileJson.Read<BridgePageInfo[]>(Call(phone, "pages", new { desktopId = desktop.Device.Id })));
        var other = new MobileBridgeMailbox();
        var foreign = MobileJson.Read<BridgeRegistration>(other.Exchange(new("register", Body: MobileJson.Element(new { name = "foreign", kind = "desktop" })), Now));
        Assert.Equal("mobile_not_found", Assert.Throws<BridgeException>(() => Call(phone, "pages", new { desktopId = foreign.Device.Id })).Code);
        Assert.Throws<BridgeException>(() => Call(phone, "send", new BridgeSend("foreign-open", foreign.Device.Id, "open", MobileJson.Element(new BridgeOpen("plugin.notes", "notes")))));
    }

    [Fact]
    public void MessagesLeaseRedeliverAndOnlyCorrectRecipientCanAck()
    {
        var desktop = Register("desktop"); var phone = Register("mobile"); PublishCatalog(desktop, phone);
        Call(phone, "send", new BridgeSend("open", desktop.Device.Id, "open", MobileJson.Element(new BridgeOpen("plugin.notes", "notes"))));
        var first = MobileJson.Read<BridgeMessage[]>(Call(desktop, "poll"));
        Assert.Single(first);
        Assert.Empty(MobileJson.Read<BridgeMessage[]>(Call(desktop, "poll", now: Now.AddSeconds(1))));
        Call(phone, "ack", new BridgeAck(first[0].Id, first[0].Lease!));
        // Open requests have a strict 30s deadline; test a result with its longer retention for redelivery.
        Publish(desktop, phone);
        var snapshot = Assert.Single(MobileJson.Read<BridgeMessage[]>(Call(phone, "poll")));
        var redelivery = Assert.Single(MobileJson.Read<BridgeMessage[]>(Call(phone, "poll", now: Now.AddSeconds(31))));
        Assert.NotEqual(snapshot.Lease, redelivery.Lease);
        Call(phone, "ack", new BridgeAck(redelivery.Id, redelivery.Lease!), Now.AddSeconds(32));
        Assert.Empty(MobileJson.Read<BridgeMessage[]>(Call(phone, "poll", now: Now.AddSeconds(63))));
    }
    [Fact]
    public void DuplicateActionHasOneMailboxEntryAndStatusSurvivesSerialization()
    {
        var desktop = Register("desktop"); var phone = Register("mobile"); PublishCatalog(desktop, phone); Publish(desktop, phone);
        var send = new BridgeSend("action", desktop.Device.Id, "action", MobileJson.Element(new BridgeInvoke("view", "fixed", [])));
        Call(phone, "send", send); Call(phone, "send", send);
        Assert.Single(MobileJson.Read<BridgeMessage[]>(Call(desktop, "poll")));
        Assert.Throws<BridgeException>(() => Call(phone, "send", send with { Body = MobileJson.Element(new BridgeInvoke("view", "different", [])) }));
        Call(desktop, "send", new BridgeSend("result", phone.Device.Id, "result", MobileJson.Element(new BridgeOutcome("action", "succeeded", "done"))));
        var restored = JsonSerializer.Deserialize<MobileBridgeMailbox>(JsonSerializer.Serialize(_mailbox, MobileJson.Options), MobileJson.Options)!;
        var status = restored.Exchange(new("status", phone.Device.Id, phone.Secret, MobileJson.Element(new { requestId = "action" })), Now);
        Assert.Equal("succeeded", status.GetProperty("status").GetString());
    }

    [Fact]
    public void LiveSnapshotsCoalesceWhileBusinessDeduplicationRemainsIntact()
    {
        var desktop = Register("desktop"); var phone = Register("mobile"); PublishCatalog(desktop, phone);
        var original = Publish(desktop, phone);
        var action = new BridgeSend("retained-action", desktop.Device.Id, "action", MobileJson.Element(new BridgeInvoke("view", "fixed", [])));
        Call(phone, "send", action);
        for (var i = 0; i < 2100; i++)
        {
            var snapshot = original with { ViewId = "view-" + i };
            Call(desktop, "send", new BridgeSend("frame-" + i, phone.Device.Id, "snapshot", MobileJson.Element(snapshot)));
        }
        var latest = Assert.Single(MobileJson.Read<BridgeMessage[]>(Call(phone, "poll")));
        Assert.Equal("view-2099", MobileJson.Read<BridgeSnapshot>(latest.Body).ViewId);
        Call(phone, "ack", new BridgeAck(latest.Id, latest.Lease!));
        Assert.Empty(MobileJson.Read<BridgeMessage[]>(Call(phone, "poll")));
        Call(phone, "send", action);
        Assert.Single(_mailbox.Messages, m => m.Message.Id == action.Id);
        Assert.Throws<BridgeException>(() => Call(phone, "send", action with { Body = MobileJson.Element(new BridgeInvoke("view", "changed", [])) }));
    }
    [Fact]
    public void ExpiredUnstartedActionsBecomeKnownFailuresInsteadOfWaitingForever()
    {
        var desktop = Register("desktop"); var phone = Register("mobile"); PublishCatalog(desktop, phone); Publish(desktop, phone);
        Call(phone, "send", new BridgeSend("expired", desktop.Device.Id, "action", MobileJson.Element(new BridgeInvoke("view", "fixed", []))));
        var status = MobileJson.Read<BridgeOutcome>(Call(phone, "status", new { requestId = "expired" }, Now.AddSeconds(31)));
        Assert.Equal("failed", status.Status);
        Assert.Empty(MobileJson.Read<BridgeMessage[]>(Call(desktop, "poll", now: Now.AddSeconds(31))));
    }
    [Fact]
    public void CatalogChangeRevocationAndOfflineDesktopRejectOldActions()
    {
        var desktop = Register("desktop"); var phone = Register("mobile"); PublishCatalog(desktop, phone); Publish(desktop, phone);
        var send = new BridgeSend("action", desktop.Device.Id, "action", MobileJson.Element(new BridgeInvoke("view", "fixed", [])));
        Assert.Equal("mobile_desktop_offline", Assert.Throws<BridgeException>(() => Call(phone, "send", send, Now.AddSeconds(46))).Code);
        Call(desktop, "catalog", new { pages = Array.Empty<BridgePageInfo>() });
        Assert.Throws<BridgeException>(() => Call(phone, "send", send));
        Call(desktop, "revoke");
        Assert.Throws<BridgeException>(() => Call(phone, "pages", new { desktopId = desktop.Device.Id }));
        Call(phone, "revoke");
        Assert.Throws<BridgeException>(() => Call(phone, "poll"));
    }
}
