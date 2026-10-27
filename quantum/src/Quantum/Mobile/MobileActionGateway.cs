using MobileNode = Quantum.Plugin.Abstraction.MobileNode;
using MobileRpcAction = Quantum.Plugin.Abstraction.MobileRpcAction;
using MobileUiValidator = Quantum.Plugin.Abstraction.MobileUiValidator;
using System.Text.Json;
using Quantum.Mobile.Internal;
using Quantum.Plugins;

namespace Quantum.Mobile;

public sealed class MobileActionGateway(PluginCatalog catalog, PluginRpcRouter router)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, View> _views = [];
    private HashSet<string> _shared = [];

    public void SetSharing(IEnumerable<string> pluginIds)
    {
        lock (_gate) { _shared = pluginIds.ToHashSet(StringComparer.Ordinal); _views.Clear(); }
    }

    public BridgePageInfo[] ShareAllPages()
    {
        lock (_gate)
        {
            var plugins = catalog.Plugins.Where(p => p.RpcRuntime?.IsActive != false && p.MobilePages.Snapshot().Length > 0)
                .Select(p => p.Manifest.Id.ToString()).ToHashSet(StringComparer.Ordinal);
            if (!_shared.SetEquals(plugins)) { _shared = plugins; _views.Clear(); }
            return Pages();
        }
    }

    public BridgePageInfo[] Pages()
    {
        lock (_gate) return catalog.Plugins.Where(p => _shared.Contains(p.Manifest.Id.ToString()) && p.RpcRuntime?.IsActive != false)
            .SelectMany(p => p.MobilePages.Snapshot().Select(page => new BridgePageInfo(p.Manifest.Id.ToString(), page.Id, page.Title) { Icon = p.Routes.Where(r => r.Definition.ShowInNavigation).OrderBy(r => r.Definition.Order).Select(r => r.Definition.Icon).FirstOrDefault() ?? "◇" })).ToArray();
    }

    public BridgeSnapshot Open(string mobileId, string desktopSessionId, BridgeOpen open, DateTimeOffset now, bool replacePrevious = true)
    {
        lock (_gate)
        {
            Purge(now);
            if (!_shared.Contains(open.PluginId)) throw new BridgeException("mobile_sharing_disabled", "Page is not shared.");
            var plugin = catalog.FindPlugin(open.PluginId) ?? throw new BridgeException("mobile_not_found", "Plugin is unavailable.");
            if (plugin.RpcRuntime?.IsActive == false) throw new BridgeException("mobile_view_stale", "Plugin is stopping.");
            var (page, revision) = plugin.MobilePages.Get(open.PageId);
            var nodes = MobileUiValidator.Validate(page);
            var actions = new Dictionary<string, BoundAction>();
            Quantum.Mobile.Internal.MobileNode Render(MobileNode node)
            {
                var id = node.Action is null ? null : Guid.NewGuid().ToString("N");
                if (node.Action is { } action)
                    actions[id!] = new(action, router.ResolveMobileTarget(action.RpcName), node.Id);
                return new(node.Type, node.Id)
                {
                    Text = node.Text,
                    Placeholder = node.Placeholder,
                    Style = node.Style,
                    MaxLength = node.MaxLength,
                    Enabled = node.Enabled,
                    ActionId = id,
                    InputIds = node.Action?.Inputs.Values.Distinct().ToArray() ?? [],
                    Children = node.Children.Select(Render).ToArray()
                };
            }
            var snapshot = new BridgeSnapshot(Guid.NewGuid().ToString("N"), desktopSessionId, open.PluginId,
                plugin.RuntimeId.ToString("N"), now.AddMinutes(5), new Quantum.Mobile.Internal.MobilePage(page.Id, page.Title, Render(page.Root)) { SchemaVersion = page.SchemaVersion });
            // Live frames remain usable until expiry so a queued action can survive disconnection.
            foreach (var key in _views.Where(v => replacePrevious && v.Value.MobileId == mobileId && v.Value.Snapshot.PluginId == open.PluginId && v.Value.Snapshot.Page.Id == open.PageId)
                         .Select(v => v.Key).ToArray())
                _views.Remove(key);
            if (_views.Count >= 1000) throw new BridgeException("mobile_busy", "Too many open pages.");
            _views[snapshot.ViewId] = new(mobileId, snapshot, revision, nodes, actions, DateTimeOffset.UtcNow.AddMinutes(5));
            return snapshot;
        }
    }

    public long Revision(BridgeOpen open)
    {
        lock (_gate)
        {
            var plugin = catalog.FindPlugin(open.PluginId);
            if (!_shared.Contains(open.PluginId) || plugin is null || plugin.RpcRuntime?.IsActive == false)
                throw new BridgeException("mobile_not_found", "Page is unavailable.");
            return plugin.MobilePages.Get(open.PageId).Revision;
        }
    }

    public async Task<BridgeOutcome> InvokeAsync(string requestId, string mobileId, BridgeInvoke invoke, CancellationToken cancellationToken,
        Func<BridgeAttachment, CancellationToken, Task<byte[]>>? readAttachment = null)
    {
        View view;
        BoundAction binding;
        JsonElement payload;
        IDisposable? ownerLease;
        var attachmentInputs = new Dictionary<string, BridgeAttachment[]>();
        Dictionary<string, JsonElement> arguments;
        lock (_gate)
        {
            Purge(DateTimeOffset.UtcNow);
            if (!_views.TryGetValue(invoke.ViewId, out view!) || view.MobileId != mobileId || !_shared.Contains(view.Snapshot.PluginId))
                throw new BridgeException("mobile_view_stale", "Refresh this page.");
            var owner = catalog.FindPlugin(view.Snapshot.PluginId);
            if (owner is null || owner.RuntimeId.ToString("N") != view.Snapshot.RuntimeId ||
                !view.Actions.TryGetValue(invoke.ActionId, out binding!))
                throw new BridgeException("mobile_action_not_allowed", "Action is no longer available.");
            if (owner.MobilePages.Revision != view.Revision)
            {
                var current = MobileUiValidator.Validate(owner.MobilePages.Get(view.Snapshot.Page.Id).Page);
                var button = view.Nodes[binding.NodeId];
                if (!current.TryGetValue(button.Id, out var liveButton) || !liveButton.Enabled ||
                    JsonSerializer.Serialize(liveButton.Action, MobileJson.Options) != JsonSerializer.Serialize(binding.Action, MobileJson.Options) ||
                    binding.Action.Inputs.Values.Any(id => !current.TryGetValue(id, out var input) || !input.Enabled ||
                        input.Type != view.Nodes[id].Type || input.MaxLength != view.Nodes[id].MaxLength))
                    throw new BridgeException("mobile_action_not_allowed", "Action is no longer available.");
            }
            if (view.Busy) throw new BridgeException("mobile_busy", "An action is already running on this page.");
            var expected = binding.Action.Inputs.Values.ToHashSet(StringComparer.Ordinal);
            if (invoke.Inputs is null || invoke.Inputs.Count != expected.Count || invoke.Inputs.Keys.Any(k => !expected.Contains(k)) ||
                JsonSerializer.SerializeToUtf8Bytes(invoke.Inputs, MobileJson.Options).Length > 16 * 1024)
                throw new BridgeException("mobile_input_invalid", "Only the declared inputs can be submitted.");
            arguments = new Dictionary<string, JsonElement>(binding.Action.Constants, StringComparer.Ordinal);
            foreach (var (field, source) in binding.Action.Inputs)
            {
                var node = view.Nodes[source];
                var value = invoke.Inputs[source];
                if (!node.Enabled || node.Type == "TextField" && (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > node.MaxLength) ||
                    node.Type == "Toggle" && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new BridgeException("mobile_input_invalid", "Input does not match its declared control.");
                if (node.Type == "Attachments")
                {
                    attachmentInputs[field] = MobileAttachments.Read(value);
                }
                arguments[field] = value.Clone();
            }
            if (!view.Nodes.Values.Any(n => n.Enabled && n.Type == "Button" && n.Action == binding.Action))
                throw new BridgeException("mobile_action_not_allowed", "Button is disabled.");
            if (attachmentInputs.Values.Sum(files => files.Length) > MobileAttachments.MaximumFiles ||
                attachmentInputs.Values.SelectMany(files => files).Sum(file => file.Size) > MobileAttachments.MaximumTotalBytes)
                throw new BridgeException("mobile_attachment_invalid", "Attachment limits exceeded.");
            payload = MobileJson.Element(arguments);
            ownerLease = owner.RpcRuntime?.AcquireMobileOwnerLease();
            view.Busy = true;
        }
        try
        {
            using (ownerLease)
            {
                foreach (var (field, files) in attachmentInputs)
                {
                    var materialized = new List<object>();
                    foreach (var file in files)
                    {
                        if (readAttachment is null) throw new BridgeException("mobile_attachment_unavailable", "Attachment transport is unavailable.");
                        var bytes = await readAttachment(file, cancellationToken);
                        if (bytes.LongLength != file.Size || Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != file.Sha256)
                            throw new BridgeException("mobile_attachment_invalid", "Attachment checksum failed.");
                        materialized.Add(new { name = file.Name, contentType = file.ContentType, dataBase64 = Convert.ToBase64String(bytes) });
                    }
                    arguments[field] = MobileJson.Element(materialized);
                }
                payload = MobileJson.Element(arguments);
                var context = new PluginRpcCallContext(view.Snapshot.PluginId, view.Snapshot.RuntimeId,
                    new Dictionary<string, JsonElement>
                    {
                        [QuantumRpcContextKeys.MobileDeviceId] = MobileJson.Element(mobileId),
                        [QuantumRpcContextKeys.MobileRequestId] = MobileJson.Element(requestId),
                        [QuantumRpcContextKeys.MobileViewId] = MobileJson.Element(invoke.ViewId)
                    });
                var dispatch = await router.InvokeMobileAsync(binding.Action.RpcName, binding.TargetRuntimeId, payload, context, cancellationToken);
                var result = dispatch.SerializedResult ?? MobileJson.Element(dispatch.Failure);
                var success = result.GetProperty("isSuccess").GetBoolean();
                return new(requestId, success ? "succeeded" : "failed", success ? "Completed." : "The plugin rejected this action.", result);
            }
        }
        finally { lock (_gate) view.Busy = false; }
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (var key in _views.Where(p => p.Value.LocalExpiresAt <= now ||
                     catalog.FindPlugin(p.Value.Snapshot.PluginId)?.RuntimeId.ToString("N") != p.Value.Snapshot.RuntimeId).Select(p => p.Key).ToArray()) _views.Remove(key);
    }
    private sealed record BoundAction(MobileRpcAction Action, Guid TargetRuntimeId, string NodeId);
    private sealed record View(string MobileId, BridgeSnapshot Snapshot, long Revision,
        IReadOnlyDictionary<string, MobileNode> Nodes, Dictionary<string, BoundAction> Actions, DateTimeOffset LocalExpiresAt)
    { public bool Busy { get; set; } }
}
