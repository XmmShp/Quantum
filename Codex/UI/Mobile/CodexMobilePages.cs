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
        var nodes = new List<MobileNode>
        {
            Ui.Text("title", "Codex").Appearance("title"),
        };
        if (result.IsSuccess)
        {
            var snapshot = result.Value!;
            nodes.Add(Ui.Text("state", L(snapshot.ConnectionState.ToString())));
            nodes.Add(Ui.Text("model", L("Model") + ": " + snapshot.SelectedModel));
            nodes.Add(Ui.Text("thread", L("Thread") + ": " + (snapshot.ThreadId ?? "—")));
            if (snapshot.IsBusy)
            {
                nodes.Add(Ui.Progress("busy"));
                nodes.Add(Ui.Text("busy-help", L("A turn is running. Progress updates automatically.")));
            }
            nodes.Add(Ui.Text("history-help", L("Showing the latest 10 messages, with long messages shortened. Open the desktop for full history.")).Appearance("caption"));
            foreach (var (message, index) in snapshot.Messages.TakeLast(10).Select((message, index) => (message, index)))
            {
                var text = message.Text.Length > 1600 ? message.Text[..1600] + "…" : message.Text;
                nodes.Add(Ui.Text("message-" + index, L(message.Role.ToString()) + ": " + text));
            }
            if (snapshot.ConnectionState == CodexConnectionState.Connected && !snapshot.IsBusy)
            {
                nodes.Add(Ui.TextField("prompt").Hint(L("Message")).Limit(2000));
                nodes.Add(Ui.Text("send-warning", L("Sending starts a model turn and may consume usage or run Quantum tools on the desktop.")).Appearance("caption"));
                nodes.Add(Ui.Button("send", L("Send to desktop Codex")).OnClick(
                    Ui.Rpc("quantum.plugin.codex.codex.send").Input("prompt", "prompt")));
            }
            else if (snapshot.ConnectionState != CodexConnectionState.Connected)
            {
                nodes.Add(Ui.Text("connect-help", L("Connect Codex and choose its working directory on the desktop first. Opening this page never starts a process.")));
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
