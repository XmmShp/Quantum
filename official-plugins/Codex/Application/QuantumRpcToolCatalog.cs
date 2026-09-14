using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantum.OfficialPlugins.Codex.Application;

internal static class QuantumRpcToolCatalog
{
    public const string Namespace = "quantum";

    public static IReadOnlyList<QuantumRpcTool> Parse(JsonElement catalog)
    {
        var tools = new List<QuantumRpcTool>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in catalog.GetProperty("services").EnumerateArray())
        {
            foreach (var method in service.GetProperty("methods").EnumerateArray())
            {
                var rpcName = method.GetProperty("qualifiedName").GetString()
                    ?? throw new InvalidOperationException("The RPC catalog contains a method without a name.");
                if (string.Equals(rpcName, "quantum.rpc.catalog", StringComparison.Ordinal))
                {
                    continue;
                }

                var toolName = CreateUniqueToolName(rpcName, names);
                var description = method.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()
                    : null;
                tools.Add(new QuantumRpcTool(
                    toolName,
                    rpcName,
                    BuildDescription(rpcName, description),
                    method.GetProperty("returnsValue").GetBoolean(),
                    NormalizeInputSchema(method.GetProperty("inputSchema"))));
            }
        }

        return tools.OrderBy(static tool => tool.RpcName, StringComparer.Ordinal).ToArray();
    }

    public static JsonArray ToDynamicTools(IReadOnlyList<QuantumRpcTool> tools)
        => new(
            new JsonObject
            {
                ["type"] = "namespace",
                ["name"] = Namespace,
                ["description"] = "Invoke capabilities exposed by the local Quantum desktop host.",
                ["tools"] = new JsonArray(tools.Select(static tool =>
                    (JsonNode)new JsonObject
                    {
                        ["type"] = "function",
                        ["name"] = tool.ToolName,
                        ["description"] = tool.Description,
                        ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText())
                    }).ToArray())
            });

    private static JsonElement NormalizeInputSchema(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText())?.AsObject() ?? new JsonObject();
        node.Remove("dotnetType");
        node.Remove("x-dotnetAttributes");
        RemoveQuantumExtensions(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void RemoveQuantumExtensions(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            if (jsonObject.Remove("nullable", out var nullable)
                && nullable is JsonValue nullableValue
                && nullableValue.TryGetValue<bool>(out var isNullable)
                && isNullable
                && jsonObject["type"] is JsonValue typeValue
                && typeValue.TryGetValue<string>(out var type))
            {
                jsonObject["type"] = new JsonArray(type, "null");
            }

            jsonObject.Remove("dotnetType");
            jsonObject.Remove("x-dotnetAttributes");
            jsonObject.Remove("recursive");
            foreach (var child in jsonObject.ToArray())
            {
                RemoveQuantumExtensions(child.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                RemoveQuantumExtensions(child);
            }
        }
    }

    private static string BuildDescription(string rpcName, string? description)
    {
        var value = string.IsNullOrWhiteSpace(description)
            ? $"Invoke the Quantum RPC '{rpcName}'."
            : $"{description.Trim()} Quantum RPC: {rpcName}.";
        return value.Length <= 1024 ? value : value[..1024];
    }

    private static string CreateUniqueToolName(string rpcName, HashSet<string> names)
    {
        var builder = new StringBuilder("rpc_");
        foreach (var character in rpcName)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : '_');
        }

        var candidate = CollapseUnderscores(builder.ToString()).TrimEnd('_');
        if (candidate.Length > 55)
        {
            candidate = $"{candidate[..46]}_{Hash(rpcName)}";
        }

        if (!names.Add(candidate))
        {
            candidate = $"{candidate[..Math.Min(candidate.Length, 46)]}_{Hash(rpcName)}";
            names.Add(candidate);
        }

        return candidate;
    }

    private static string CollapseUnderscores(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasUnderscore = false;
        foreach (var character in value)
        {
            if (character == '_')
            {
                if (!previousWasUnderscore)
                {
                    builder.Append(character);
                }

                previousWasUnderscore = true;
            }
            else
            {
                builder.Append(character);
                previousWasUnderscore = false;
            }
        }

        return builder.ToString();
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
}
