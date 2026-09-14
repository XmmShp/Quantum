using System.Text.Json;
using Quantum.OfficialPlugins.Codex.Application;

namespace Quantum.Tests;

public sealed class CodexPluginTests
{
    [Fact]
    public void RpcCatalogBecomesNamespacedCodexTools()
    {
        var catalog = JsonSerializer.SerializeToElement(new
        {
            services = new object[]
            {
                new
                {
                    methods = new object[]
                    {
                        Method("quantum.rpc.catalog", "Catalog", returnsValue: true),
                        Method(
                            "quantum.plugin.calendar.calendar.create",
                            "Create a calendar item.",
                            returnsValue: true)
                    }
                }
            }
        });

        var tool = Assert.Single(QuantumRpcToolCatalog.Parse(catalog));

        Assert.Equal("quantum.plugin.calendar.calendar.create", tool.RpcName);
        Assert.StartsWith("rpc_", tool.ToolName, StringComparison.Ordinal);
        Assert.Contains("Create a calendar item.", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnetType", tool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("x-dotnetAttributes", tool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(
            ["string", "null"],
            tool.InputSchema.GetProperty("properties").GetProperty("value").GetProperty("type")
                .EnumerateArray().Select(static type => type.GetString()));

        var dynamicTools = QuantumRpcToolCatalog.ToDynamicTools([tool]);
        var toolNamespace = Assert.Single(dynamicTools);
        Assert.Equal("namespace", toolNamespace!["type"]!.GetValue<string>());
        Assert.Equal("quantum", toolNamespace["name"]!.GetValue<string>());
        Assert.Equal(tool.ToolName, toolNamespace["tools"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void LongAndCollidingRpcNamesProduceValidStableToolNames()
    {
        var longName = $"quantum.plugin.{new string('a', 90)}.service.invoke";
        var catalog = JsonSerializer.SerializeToElement(new
        {
            services = new object[]
            {
                new
                {
                    methods = new object[]
                    {
                        Method("quantum.plugin.sample.a-b", null, returnsValue: false),
                        Method("quantum.plugin.sample.a_b", null, returnsValue: false),
                        Method(longName, null, returnsValue: false)
                    }
                }
            }
        });

        var tools = QuantumRpcToolCatalog.Parse(catalog);

        Assert.Equal(3, tools.Select(static tool => tool.ToolName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(tools, static tool =>
        {
            Assert.InRange(tool.ToolName.Length, 1, 64);
            Assert.Matches("^[a-z0-9_]+$", tool.ToolName);
        });
    }

    private static object Method(string name, string? description, bool returnsValue)
        => new
        {
            qualifiedName = name,
            description,
            returnsValue,
            inputSchema = JsonSerializer.Deserialize<JsonElement>(
                """
                {
                  "type": "object",
                  "dotnetType": "Example.Request",
                  "properties": {
                    "value": {
                      "type": "string",
                      "nullable": true,
                      "dotnetType": "System.String",
                      "x-dotnetAttributes": []
                    }
                  }
                }
                """)
        };
}
