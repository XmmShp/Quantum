using System.Text.Json;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

internal sealed class CapturedMobilePages : IQuantumMobilePages
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, MobilePage> Pages { get; } = [];
    public void Publish(MobilePage page)
    {
        MobileUiValidator.Validate(page);
        Pages[page.Id] = page;
    }
    public void Remove(string pageId) => Pages.TryRemove(pageId, out _);
    public MobileNode Node(string pageId, string nodeId) => MobileUiValidator.Validate(Pages[pageId])[nodeId];
    public async Task<JsonElement> Click(OfficialRpcHarness host, string pageId, string nodeId, Dictionary<string, JsonElement>? inputs = null)
    {
        var action = Assert.IsType<MobileRpcAction>(Node(pageId, nodeId).Action);
        var payload = new Dictionary<string, JsonElement>(action.Constants);
        foreach (var (field, source) in action.Inputs)
        {
            payload[field] = inputs![source];
        }
        return await host.Call(action.RpcName, payload, expectsValue: nodeId != "refresh");
    }
}
