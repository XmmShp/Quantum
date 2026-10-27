using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Codex.UI.Localization;
using Quantum.Plugin.Abstraction;
using System.Globalization;
using System.Resources;
using Ui = Quantum.Plugin.Abstraction.Mobile;

namespace Quantum.OfficialPlugins.Codex.UI.Mobile;

public sealed class CodexMobilePages : ICodexMobilePages
{
    private static readonly ResourceManager Resources = new(typeof(PluginStrings));
    private readonly ICodexRpcServiceClient _client;
    private readonly IQuantumMobilePages? _pages;

    public CodexMobilePages(ICodexRpcServiceClient client, IQuantumMobilePages? pages = null)
    {
        _client = client;
        _pages = pages;
    }

    public async Task PublishAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pages is null)
        {
            throw new DomainException("mobile_unavailable", "This host does not support mobile pages.");
        }
        var result = await _client.GetSnapshotAsync(new(), Context.Empty, cancellationToken);
        var nodes = new List<MobileNode>();
        if (result.IsSuccess)
        {
            var snapshot = result.Value!;
            nodes.Add(Ui.HStack("connection", Ui.Text("state", L(snapshot.ConnectionState.ToString())).Appearance("caption"), Ui.Text("model", snapshot.SelectedModel ?? "—").Appearance("caption")));
            nodes.Add(Ui.Text("thread", L("Using the current desktop conversation.")).Appearance("caption"));
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                nodes.Add(Ui.Text("runtime-error", L(snapshot.ErrorMessage)).Appearance("error"));
            }
            if (snapshot.IsBusy)
            {
                nodes.Add(Ui.HStack("working", Ui.Progress("busy"), Ui.Text("busy-help", L("A turn is running. Progress updates automatically.")).Appearance("caption")));
            }
            var history = new List<MobileNode> { Ui.Text("history-title", L("Recent messages")).Appearance("title") };
            foreach (var (message, index) in snapshot.Messages.TakeLast(10).Select((message, index) => (message, index)))
            {
                var text = message.Text.Length > 1600 ? message.Text[..1600] + "…" : message.Text;
                if (message.IsTranscribing)
                {
                    text += "\n" + L("Transcribing voice…");
                }

                if (message.Attachments.Length > 0)
                {
                    text += "\n" + string.Join(" · ", message.Attachments.Select(f => "📎 " + f.Name));
                }

                history.Add(Ui.VStack("chat-row-" + index,
                    Ui.HStack("chat-meta-" + index, Ui.Text("role-" + index, L(message.Role.ToString())).Appearance("caption"), Ui.Text("time-" + index, message.CreatedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)).Appearance("caption")),
                    Ui.Text("message-" + index, text)));
                history.Add(Ui.Divider("chat-line-" + index));
            }
            if (snapshot.Messages.Count == 0)
            {
                history.Add(Ui.Text("empty", L("No items to display.")).Appearance("caption"));
            }
            if (history.Last().Type == "Divider")
            {
                history.RemoveAt(history.Count - 1);
            }
            nodes.Add(Ui.VStack("history", history.ToArray()));
            if (snapshot.ConnectionState == CodexConnectionState.Connected)
            {
                var send = Ui.Button("send", L("Send")).OnClick(Ui.Rpc("quantum.plugin.codex.codex.send").Input("prompt", "prompt").Input("attachments", "attachments").Constant("speechLanguage", CultureInfo.CurrentUICulture.Name));
                send.Enabled = !snapshot.IsBusy;
                nodes.Add(Ui.VStack("compose",
                    Ui.Text("compose-title", L("Continue conversation")).Appearance("title"),
                    Ui.Attachments("attachments"),
                    Ui.HStack("compose-row", Ui.TextField("prompt").Hint(L("Message")).Limit(2000),
                        send),
                    Ui.Text("send-warning", L("Sending starts a model turn and may consume usage or run Quantum tools on the desktop.")).Appearance("caption")));
            }
            else if (snapshot.ConnectionState != CodexConnectionState.Connected)
            {
                nodes.Add(Ui.Text("connect-help", L("Connect Codex and choose its working directory on the desktop first. Opening this page never starts a process.")).Appearance("caption"));
            }
        }
        else
        {
            nodes.Add(Ui.Text("load-error", L("Unable to load data. Reconnecting automatically.")).Appearance("error"));
        }
        _pages.Publish(Ui.Page("codex", "Codex", Ui.Scroll("scroll", Ui.VStack("root", nodes.ToArray()))));
    }

    private static string L(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
