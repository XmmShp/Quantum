using Quantum.OfficialPlugins.Codex;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Codex.UI.Pages;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Quantum.Tests;

public sealed class CodexPluginTests
{
    [Fact]
    public void AppServerStreamsPreserveUnicodeAndJsonEscapes()
    {
        var startInfo = CodexAppServerClient.CreateStartInfo();
        // Decoding these UTF-8 bytes as GBK consumes the backslash after “：”,
        // leaving an unescaped quote and causing "'e' is invalid after a value".
        const string line = """{"method":"error","params":{"message":"中文说明：\"error\"，路径 C:\\工作区 😀"}}""";
        var wireBytes = Encoding.UTF8.GetBytes(line);

        foreach (var encoding in new[]
        {
            startInfo.StandardInputEncoding,
            startInfo.StandardOutputEncoding,
            startInfo.StandardErrorEncoding
        })
        {
            Assert.NotNull(encoding);
            Assert.Empty(encoding.GetPreamble());
            Assert.Equal(wireBytes, encoding.GetBytes(line));
            using var document = JsonDocument.Parse(encoding.GetString(wireBytes));
            Assert.Equal(
                "中文说明：\"error\"，路径 C:\\工作区 😀",
                document.RootElement.GetProperty("params").GetProperty("message").GetString());
        }
    }

    [Fact]
    public void LunaIsTheDefaultModel()
    {
        Assert.Equal("gpt-5.6-luna", CodexRuntime.DefaultModel);
        Assert.Equal(
            "gpt-5.6-luna",
            CodexRuntime.ResolveModel(
                CodexRuntime.DefaultModel,
                CodexRuntime.InitialModels));
    }

    [Fact]
    public void UnavailableModelIsRejectedBeforeStartingAThread()
    {
        var exception = Assert.Throws<NOF.Domain.DomainException>(() =>
            CodexRuntime.ResolveModel("gpt-missing", CodexRuntime.InitialModels));

        Assert.Contains("not available", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedUpgradeErrorBecomesActionableMessage()
    {
        const string response =
            "{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\","
            + "\"message\":\"The 'gpt-5.6-sol' model requires a newer version of Codex.\"}}";

        var message = CodexErrorMessages.Normalize(response);

        Assert.Contains("npm install -g @openai/codex@latest", message, StringComparison.Ordinal);
        Assert.Contains("disconnect and reconnect", message, StringComparison.Ordinal);
        Assert.DoesNotContain(message, character => character is >= '\u4e00' and <= '\u9fff');
        Assert.DoesNotContain("invalid_request_error", message, StringComparison.Ordinal);
        var resources = new System.Resources.ResourceManager(typeof(OfficialPlugins.Codex.UI.Localization.PluginStrings));
        var localized = resources.GetString(message, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        Assert.NotNull(localized);
        Assert.Contains("断开并重新连接", localized, StringComparison.Ordinal);
    }

    [Fact]
    public void PageUsesConstructorInjectionForPluginOwnedServices()
    {
        var constructor = Assert.Single(typeof(Codex).GetConstructors());

        Assert.Equal(new[] { typeof(ICodexRpcServiceClient), typeof(ICodexRuntimeEvents) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void DrawerUsesConstructorInjectionForSharedPluginState()
    {
        var constructor = Assert.Single(typeof(CodexDrawer).GetConstructors());

        Assert.Equal(new[] { typeof(ICodexRpcServiceClient), typeof(ICodexRuntimeEvents) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void ManifestRegistersNativeGlobalDrawer()
    {
        var manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            "plugin-manifests",
            "codex.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        var drawer = Assert.Single(
            root.GetProperty("ui").GetProperty("globalComponents").EnumerateArray());
        Assert.Equal(
            typeof(CodexDrawer).FullName,
            drawer.GetProperty("component").GetString());
        Assert.Empty(root.GetProperty("web").GetProperty("postBlazor").EnumerateArray());
    }

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
    public void EmptyRpcCatalogDoesNotCreateAnEmptyNamespace()
    {
        var dynamicTools = QuantumRpcToolCatalog.ToDynamicTools([]);

        Assert.Empty(dynamicTools);
    }

    [Fact]
    public void IntegrationContractDoesNotExposeToolApprovalState()
    {
        Assert.DoesNotContain(
            typeof(ICodexRuntime).GetMethods(),
            method => method.Name.Contains("ResolveToolCall", StringComparison.Ordinal));
        Assert.Null(typeof(CodexIntegrationSnapshot).GetProperty("PendingCalls"));
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
