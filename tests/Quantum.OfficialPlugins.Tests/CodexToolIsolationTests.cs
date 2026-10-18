using Quantum.OfficialPlugins.Codex.Application;
using System.Text.Json;
using Xunit;

namespace Quantum.Tests;

public sealed class CodexToolIsolationTests
{
    [Fact]
    public void SelfControlRpcMethodsAreNotInjectedIntoTheSameCodexSession()
    {
        var catalog = JsonSerializer.SerializeToElement(new
        {
            services = new[]
            {
                new
                {
                    methods = new[]
                    {
                        Method("quantum.rpc.catalog"),
                        Method("quantum.plugin.codex.codex.send"),
                        Method("QUANTUM.PLUGIN.CODEX.codex.disconnect"),
                        Method("quantum.plugin.ledger.ledger.list"),
                        Method("quantum.plugin.calendar.calendar.create")
                    }
                }
            }
        });
        var tools = QuantumRpcToolCatalog.Parse(catalog);
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, tool => tool.RpcName == "quantum.plugin.ledger.ledger.list");
        Assert.Contains(tools, tool => tool.RpcName == "quantum.plugin.calendar.calendar.create");
    }

    private static object Method(string name) => new
    {
        qualifiedName = name,
        description = name,
        returnsValue = true,
        inputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
            }
        })
    };
}
