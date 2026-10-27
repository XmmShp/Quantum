using MobileUiValidator = Quantum.Mobile.Internal.MobileUiValidator;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantum.Mobile.Internal;

namespace Quantum.Platform.Application;

/// <summary>Pure account-scoped state machine. Caller identity is resolved before loading this aggregate.</summary>
public sealed class MobileBridgeMailbox
{
    public List<DeviceState> Devices { get; set; } = [];
    // Retained only for deserializing previously persisted mailboxes. Account ownership now grants access.
    public List<BridgeBinding> Bindings { get; set; } = [];
    public List<MessageState> Messages { get; set; } = [];
    public Dictionary<string, OutcomeState> Outcomes { get; set; } = [];
    public Dictionary<string, SnapshotState> Snapshots { get; set; } = [];

    public JsonElement Exchange(BridgeRequest request, DateTimeOffset now)
    {
        Cleanup(now);
        if (request.Body.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(request.Body.GetRawText()) > 256 * 1024)
            throw new ArgumentException("Invalid mobile request size or body.");
        var body = request.Body;
        if (request.Operation == "register")
        {
            var name = Text(body, "name");
            var kind = Text(body, "kind");
            var instance = Instance(body);
            var existing = instance is null ? null : Devices.SingleOrDefault(d => d.ClientInstanceId == instance && d.Device.Kind == kind);
            if (name.Length > 80 || kind is not ("desktop" or "mobile") || existing is null && Devices.Count >= 20)
                throw new ArgumentException("Invalid device or device quota exceeded.");
            var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var device = existing ?? new DeviceState
            {
                Device = new(Guid.NewGuid().ToString("N"), name, kind, now, Guid.NewGuid().ToString("N")),
                SecretHash = Hash(secret), ExpiresAt = now.AddHours(24)
            };
            device.ClientInstanceId = instance;
            device.SecretHash = Hash(secret);
            device.Device = device.Device with { Name = name, SeenAt = now };
            device.ExpiresAt = now.AddHours(24);
            if (existing is null) Devices.Add(device);
            return MobileJson.Element(new BridgeRegistration(device.Device, secret));
        }
        var sender = Devices.SingleOrDefault(d => d.Device.Id == request.DeviceId && d.ExpiresAt > now);
        if (sender is null || request.DeviceSecret is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(sender.SecretHash), Encoding.ASCII.GetBytes(Hash(request.DeviceSecret))))
            throw new BridgeException("mobile_device_revoked", "Register this device again.");
        sender.Device = sender.Device with { SeenAt = now };
        sender.ExpiresAt = now.AddHours(24);
        if (request.Operation == "heartbeat" && Instance(body) is { } instanceId)
            sender.ClientInstanceId = instanceId;
        switch (request.Operation)
        {
            case "heartbeat": return MobileJson.Element(new { serverTime = now });
            case "devices": return MobileJson.Element(Devices.Select(d => d.Device).ToArray());
            // Compatibility for older clients: every live same-account pair is accessible.
            case "bindings": return MobileJson.Element(Devices.Where(d => d.Device.Kind != sender.Device.Kind)
                .Select(d => sender.Device.Kind == "desktop"
                    ? new BridgeBinding(sender.Device.Id, d.Device.Id, d.Device.Name, true)
                    : new BridgeBinding(d.Device.Id, sender.Device.Id, sender.Device.Name, true)).ToArray());
            case "bind":
            {
                RequireKind(sender, "mobile");
                _ = Target(Text(body, "desktopId"), "desktop");
                return MobileJson.Element(new { });
            }
            case "approve":
            {
                throw new BridgeException("mobile_approval_not_required", "Same-account access does not require device approval.");
            }
            case "catalog":
            {
                RequireKind(sender, "desktop");
                var pages = MobileJson.Read<BridgePageInfo[]>(body.GetProperty("pages"));
                if (pages.Length > 100) throw new ArgumentException("Too many mobile pages.");
                foreach (var page in pages)
                {
                    MobileUiValidator.Id(page.PluginId); MobileUiValidator.Id(page.PageId);
                    if (string.IsNullOrWhiteSpace(page.Title) || page.Title.Length > 200) throw new ArgumentException("Invalid page title.");
                    if (page.Icon is { } icon && (icon.Length > 64 || icon.Any(char.IsControl))) throw new ArgumentException("Invalid page icon.");
                }
                sender.Pages = pages;
                // A catalog publication is an authorization epoch change. Invalidate old phone views.
                foreach (var key in Snapshots.Where(p => p.Value.DesktopId == sender.Device.Id).Select(p => p.Key).ToArray()) Snapshots.Remove(key);
                return MobileJson.Element(new { });
            }
            case "pages":
            {
                RequireKind(sender, "mobile");
                var desktop = Target(Text(body, "desktopId"), "desktop");
                var includeIcons = body.TryGetProperty("includeIcons", out var icons) && icons.ValueKind == JsonValueKind.True;
                return MobileJson.Element(includeIcons ? desktop.Pages : desktop.Pages.Select(p => p with { Icon = null }).ToArray());
            }
            case "send": return Send(sender, MobileJson.Read<BridgeSend>(body), now);
            case "authorize":
            {
                RequireKind(sender, "desktop");
                var requestId = Text(body, "requestId");
                var command = Messages.SingleOrDefault(m => m.Message.Id == requestId && m.Message.TargetId == sender.Device.Id && m.Message.Kind == "action");
                if (command is null || command.Message.ExpiresAt <= now) throw new BridgeException("mobile_action_expired", "Action expired.");
                RequireDevicePair(sender.Device.Id, command.Message.SenderId);
                var invoke = MobileJson.Read<BridgeInvoke>(command.Message.Body);
                if (!Snapshots.Values.Any(s => s.DesktopId == sender.Device.Id && s.MobileId == command.Message.SenderId && s.Snapshot.ViewId == invoke.ViewId && s.Snapshot.ExpiresAt > now))
                    throw new BridgeException("mobile_view_stale", "Refresh this page.");
                var status = Outcomes[requestId];
                if (status.Outcome.Status != "accepted") throw new BridgeException("mobile_request_conflict", "Action was already authorized.");
                Outcomes[requestId] = status with { Outcome = new(requestId, "running", "Desktop is processing this action.") };
                return MobileJson.Element(new { authorized = true });
            }
            case "poll":
            {
                var batch = Messages.Where(m => m.Message.TargetId == sender.Device.Id && !m.Acknowledged && m.LeaseUntil <= now)
                    .OrderBy(m => m.CreatedAt).Take(50).ToArray();
                var bytes = 0;
                batch = batch.TakeWhile(message => (bytes += Encoding.UTF8.GetByteCount(message.Message.Body.GetRawText()) + 1024) <= 512 * 1024).ToArray();
                foreach (var message in batch)
                {
                    message.Message = message.Message with { Lease = Guid.NewGuid().ToString("N") };
                    message.LeaseUntil = now.AddSeconds(30);
                }
                return MobileJson.Element(batch.Select(m => m.Message).ToArray());
            }
            case "ack":
            {
                var ack = MobileJson.Read<BridgeAck>(body);
                var message = Messages.SingleOrDefault(m => m.Message.Id == ack.Id && m.Message.TargetId == sender.Device.Id);
                if (message is not null && message.Message.Lease == ack.Lease && message.LeaseUntil > now) message.Acknowledged = true;
                return MobileJson.Element(new { });
            }
            case "status":
            {
                var id = Text(body, "requestId");
                if (!Outcomes.TryGetValue(id, out var status) || status.MobileId != sender.Device.Id)
                    throw new BridgeException("mobile_not_found", "Action status is unavailable.");
                return MobileJson.Element(status.Outcome);
            }
            case "snapshot":
            {
                RequireKind(sender, "mobile");
                var desktopId = Text(body, "desktopId");
                RequireDevicePair(desktopId, sender.Device.Id);
                var snapshot = Snapshots.Values.OrderByDescending(s => s.Snapshot.ExpiresAt).FirstOrDefault(s => s.DesktopId == desktopId && s.MobileId == sender.Device.Id &&
                    s.Snapshot.PluginId == Text(body, "pluginId") && s.Snapshot.Page.Id == Text(body, "pageId"));
                return MobileJson.Element(snapshot?.Snapshot);
            }
            case "revoke":
                Devices.Remove(sender);
                Bindings.RemoveAll(b => b.DesktopId == sender.Device.Id || b.MobileId == sender.Device.Id);
                Messages.RemoveAll(m => m.Message.SenderId == sender.Device.Id || m.Message.TargetId == sender.Device.Id);
                foreach (var key in Snapshots.Where(p => p.Value.DesktopId == sender.Device.Id || p.Value.MobileId == sender.Device.Id).Select(p => p.Key).ToArray()) Snapshots.Remove(key);
                foreach (var key in Outcomes.Where(p => p.Value.DesktopId == sender.Device.Id || p.Value.MobileId == sender.Device.Id).Select(p => p.Key).ToArray()) Outcomes.Remove(key);
                return MobileJson.Element(new { });
            default: throw new ArgumentException("Unknown mobile bridge operation.");
        }
    }

    private JsonElement Send(DeviceState sender, BridgeSend send, DateTimeOffset now)
    {
        MobileUiValidator.Id(send.Id);
        var target = Target(send.TargetId, sender.Device.Kind == "mobile" ? "desktop" : "mobile");
        var desktop = sender.Device.Kind == "desktop" ? sender : target;
        var mobile = sender.Device.Kind == "mobile" ? sender : target;
        var duplicate = Messages.FirstOrDefault(m => m.Message.Id == send.Id);
        if (duplicate is not null)
        {
            if (duplicate.Message.SenderId != sender.Device.Id || duplicate.Message.TargetId != target.Device.Id ||
                duplicate.Message.Kind != send.Kind || duplicate.Message.Body.GetRawText() != send.Body.GetRawText())
                throw new BridgeException("mobile_request_conflict", "Message ID has different contents.");
            return MobileJson.Element(new { accepted = true });
        }
        if (Messages.Count >= 2000 || Outcomes.Count >= 2000) throw new BridgeException("mobile_rate_limited", "Mailbox quota reached.");
        var expiresAt = now.AddHours(24);
        if (sender.Device.Kind == "mobile")
        {
            if (send.Kind == "open")
            {
                expiresAt = now.AddSeconds(30);
                var open = MobileJson.Read<BridgeOpen>(send.Body);
                if (!desktop.Pages.Any(p => p.PluginId == open.PluginId && p.PageId == open.PageId))
                    throw new BridgeException("mobile_sharing_disabled", "Page is not shared.");
            }
            else if (send.Kind == "action")
            {
                var invoke = MobileJson.Read<BridgeInvoke>(send.Body);
                var snapshot = Snapshots.Values.FirstOrDefault(s => s.DesktopId == desktop.Device.Id && s.MobileId == mobile.Device.Id && s.Snapshot.ViewId == invoke.ViewId)?.Snapshot;
                if (snapshot is null || snapshot.ExpiresAt <= now || !desktop.Pages.Any(p => p.PluginId == snapshot.PluginId && p.PageId == snapshot.Page.Id))
                    throw new BridgeException("mobile_view_stale", "Refresh this page.");
                if (invoke.Inputs is null || Encoding.UTF8.GetByteCount(send.Body.GetRawText()) > 16 * 1024)
                    throw new ArgumentException("Invalid action inputs.");
                if (Outcomes.ContainsKey(send.Id)) throw new BridgeException("mobile_request_conflict", "Action ID has already been used.");
                expiresAt = snapshot.ExpiresAt;
                Outcomes[send.Id] = new(desktop.Device.Id, mobile.Device.Id, now.AddHours(24), new(send.Id, "accepted", "Waiting for desktop."));
            }
            else throw new ArgumentException("Unsupported mobile message kind.");
        }
        else if (send.Kind == "snapshot")
        {
            var snapshot = MobileJson.Read<BridgeSnapshot>(send.Body);
            MobileUiValidator.Validate(snapshot.Page, wire: true);
            if (snapshot.DesktopSessionId != desktop.Device.SessionId || snapshot.ExpiresAt <= now || snapshot.ExpiresAt > now.AddMinutes(5) ||
                !desktop.Pages.Any(p => p.PluginId == snapshot.PluginId && p.PageId == snapshot.Page.Id))
                throw new ArgumentException("Invalid mobile snapshot.");
            // A small overlap lets an action already in transit use the preceding frame.
            // The desktop still validates its action declaration against the current page.
            foreach (var key in Snapshots.Where(s => !Messages.Any(m => m.Message.Kind == "action" &&
                         Outcomes.TryGetValue(m.Message.Id, out var outcome) && outcome.Outcome.Status is "accepted" or "running" &&
                         MobileJson.Read<BridgeInvoke>(m.Message.Body).ViewId == s.Value.Snapshot.ViewId) && s.Value.DesktopId == desktop.Device.Id && s.Value.MobileId == mobile.Device.Id &&
                         s.Value.Snapshot.PluginId == snapshot.PluginId && s.Value.Snapshot.Page.Id == snapshot.Page.Id)
                         .OrderByDescending(s => s.Value.Snapshot.ExpiresAt).Skip(2).Select(s => s.Key).ToArray())
                Snapshots.Remove(key);
            Snapshots[$"{desktop.Device.Id}:{mobile.Device.Id}:{snapshot.PluginId}:{snapshot.Page.Id}:{snapshot.ViewId}"] = new(desktop.Device.Id, mobile.Device.Id, snapshot);
            // Page snapshots are replaceable state, not business commands. Keep only the latest
            // queued frame for this page, including when a suspended phone has not acknowledged it.
            Messages.RemoveAll(m => m.Message.SenderId == desktop.Device.Id && m.Message.TargetId == mobile.Device.Id &&
                m.Message.Kind == "snapshot" && MobileJson.Read<BridgeSnapshot>(m.Message.Body) is var prior &&
                prior.PluginId == snapshot.PluginId && prior.Page.Id == snapshot.Page.Id);
        }
        else if (send.Kind == "pageError")
        {
            var error = MobileJson.Read<BridgePageError>(send.Body);
            if (!Messages.Any(m => m.Message.Id == error.RequestId && m.Message.SenderId == mobile.Device.Id && m.Message.TargetId == desktop.Device.Id && m.Message.Kind == "open"))
                throw new BridgeException("mobile_not_found", "Page request does not belong to this device pair.");
        }
        else if (send.Kind == "result")
        {
            var outcome = MobileJson.Read<BridgeOutcome>(send.Body);
            if (!Outcomes.TryGetValue(outcome.RequestId, out var prior) || prior.DesktopId != desktop.Device.Id || prior.MobileId != mobile.Device.Id)
                throw new BridgeException("mobile_not_found", "Action does not belong to this device pair.");
            if (outcome.Status is not ("succeeded" or "failed" or "unknown")) throw new ArgumentException("Invalid outcome.");
            if (prior.Outcome.Status is not ("accepted" or "running" or "unknown") && prior.Outcome != outcome)
                throw new BridgeException("mobile_request_conflict", "Action is already terminal.");
            Outcomes[outcome.RequestId] = prior with { Outcome = outcome };
        }
        else throw new ArgumentException("Unsupported desktop message kind.");
        Messages.Add(new() { Message = new(send.Id, sender.Device.Id, target.Device.Id, send.Kind, send.Body.Clone(), expiresAt), CreatedAt = now });
        return MobileJson.Element(new { accepted = true });
    }

    private DeviceState Target(string id, string kind) => Devices.SingleOrDefault(d => d.Device.Id == id && d.Device.Kind == kind)
        ?? throw new BridgeException("mobile_not_found", "Target device is unavailable.");
    private static void RequireKind(DeviceState device, string kind)
    {
        if (device.Device.Kind != kind) throw new BridgeException("mobile_forbidden", "Operation is not available to this device type.");
    }
    private void RequireDevicePair(string desktopId, string mobileId)
    {
        _ = Target(desktopId, "desktop");
        _ = Target(mobileId, "mobile");
    }
    private void Cleanup(DateTimeOffset now)
    {
        Devices.RemoveAll(d => d.ExpiresAt <= now);
        Bindings.RemoveAll(b => !Devices.Any(d => d.Device.Id == b.DesktopId) || !Devices.Any(d => d.Device.Id == b.MobileId));
        // Business commands and results retain their 24h deduplication window. Transport-only
        // page frames and completed opens need not accumulate during a live subscription.
        Messages.RemoveAll(m => m.CreatedAt < now.AddHours(-24) ||
            m.Acknowledged && m.Message.Kind is "snapshot" or "open" or "pageError");
        foreach (var message in Messages.Where(m => m.Message.ExpiresAt <= now))
        {
            message.Acknowledged = true;
            if (message.Message.Kind != "action" || !Outcomes.TryGetValue(message.Message.Id, out var status)) continue;
            if (status.Outcome.Status == "accepted")
                Outcomes[message.Message.Id] = status with { Outcome = new(message.Message.Id, "failed", "Action expired before desktop execution.") };
            else if (status.Outcome.Status == "running" && message.Message.ExpiresAt.AddSeconds(30) <= now)
                Outcomes[message.Message.Id] = status with { Outcome = new(message.Message.Id, "unknown", "Desktop execution has not been confirmed. Check desktop state.") };
        }
        foreach (var key in Outcomes.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray()) Outcomes.Remove(key);
        foreach (var key in Snapshots.Where(p => p.Value.Snapshot.ExpiresAt <= now).Select(p => p.Key).ToArray()) Snapshots.Remove(key);
    }
    private static string? Instance(JsonElement body)
    {
        if (!body.TryGetProperty("clientInstanceId", out var value)) return null;
        return value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "N", out var id)
            ? id.ToString("N") : throw new ArgumentException("Invalid client instance ID.");
    }
    private static string Text(JsonElement body, string name)
    {
        var value = body.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 256 ? value : throw new ArgumentException("Invalid " + name);
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public sealed class DeviceState
    {
        public string? ClientInstanceId { get; set; }
        public required BridgeDevice Device { get; set; }
        public required string SecretHash { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public BridgePageInfo[] Pages { get; set; } = [];
    }
    public sealed class MessageState
    {
        public required BridgeMessage Message { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LeaseUntil { get; set; }
        public bool Acknowledged { get; set; }
    }
    public sealed record OutcomeState(string DesktopId, string MobileId, DateTimeOffset ExpiresAt, BridgeOutcome Outcome);
    public sealed record SnapshotState(string DesktopId, string MobileId, BridgeSnapshot Snapshot);
}
