using System.Text.Json;

namespace Quantum.Plugin.Abstraction;

public sealed record MobileRpcAction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public required string RpcName { get; set; }
    public Dictionary<string, JsonElement> Constants { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.Ordinal);
    public MobileRpcAction Constant<T>(string field, T value)
    {
        // Do not cache collectible plugin CLR types in the protocol serializer's process-wide metadata.
        if (value is not null and not JsonElement and not string and not bool and not byte and not sbyte and not short and not ushort
            and not int and not uint and not long and not ulong and not float and not double and not decimal)
            throw new ArgumentException("Constants must be JSON primitives or JsonElement, not plugin-owned CLR objects.");
        return this with { Constants = new(Constants, StringComparer.Ordinal) { [field] = JsonSerializer.SerializeToElement(value, JsonOptions) } };
    }
    public MobileRpcAction Input(string field, string nodeId) => this with
    {
        Inputs = new(Inputs, StringComparer.Ordinal) { [field] = nodeId }
    };
}

