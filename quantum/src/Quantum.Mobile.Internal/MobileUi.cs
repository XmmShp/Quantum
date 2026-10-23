using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Quantum.Mobile.Internal;

public static class MobileJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 48,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    public static T Read<T>(JsonElement value) => value.Deserialize<T>(Options)
        ?? throw new ArgumentException("A non-null payload is required.");
}

public sealed record MobileNode(string Type, string Id)
{
    public string? Text { get; init; }
    public string? Placeholder { get; init; }
    public string Style { get; init; } = "body";
    public int MaxLength { get; init; } = 500;
    public bool Enabled { get; init; } = true;
    public string? ActionId { get; init; }
    public string[] InputIds { get; init; } = [];
    public MobileRpcAction? Action { get; init; }
    public MobileNode[] Children { get; init; } = [];
    public MobileNode Label(string text) => this with { Text = text };
    public MobileNode Hint(string text) => this with { Placeholder = text };
    public MobileNode Limit(int length) => this with { MaxLength = length };
    public MobileNode Appearance(string style) => this with { Style = style };
    public MobileNode OnClick(MobileRpcAction action) => this with { Action = action };
}

public sealed record MobileRpcAction(string RpcName)
{
    public Dictionary<string, JsonElement> Constants { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.Ordinal);
    public MobileRpcAction Constant<T>(string field, T value)
    {
        // Do not cache collectible plugin CLR types in the protocol serializer's process-wide metadata.
        if (value is not null and not JsonElement and not string and not bool and not byte and not sbyte and not short and not ushort
            and not int and not uint and not long and not ulong and not float and not double and not decimal)
            throw new ArgumentException("Constants must be JSON primitives or JsonElement, not plugin-owned CLR objects.");
        return this with { Constants = new(Constants, StringComparer.Ordinal) { [field] = MobileJson.Element(value) } };
    }
    public MobileRpcAction Input(string field, string nodeId) => this with
    {
        Inputs = new(Inputs, StringComparer.Ordinal) { [field] = nodeId }
    };
}

public sealed record MobilePage(string Id, string Title, MobileNode Root)
{
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Executed only in the desktop plugin, never interpreted by the mobile client.</summary>
public static class Mobile
{
    public static MobilePage Page(string id, string title, MobileNode root) => new(id, title, root);
    public static MobileNode VStack(string id, params MobileNode[] children) => new("VStack", id) { Children = children };
    public static MobileNode HStack(string id, params MobileNode[] children) => new("HStack", id) { Children = children };
    public static MobileNode Scroll(string id, MobileNode child) => new("Scroll", id) { Children = [child] };
    public static MobileNode Text(string id, string text) => new("Text", id) { Text = text };
    public static MobileNode TextField(string id) => new("TextField", id);
    public static MobileNode Toggle(string id, string text) => new("Toggle", id) { Text = text };
    public static MobileNode Button(string id, string text) => new("Button", id) { Text = text };
    public static MobileNode Divider(string id) => new("Divider", id);
    public static MobileNode Spacer(string id) => new("Spacer", id);
    public static MobileNode Progress(string id) => new("Progress", id);
    public static MobileRpcAction Rpc(string qualifiedName) => new(qualifiedName);
}

public static partial class MobileUiValidator
{
    public const int MaximumPageBytes = 128 * 1024;
    private static readonly HashSet<string> Types = ["VStack", "HStack", "Scroll", "Text", "TextField", "Toggle", "Button", "Divider", "Spacer", "Progress"];
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    public static void Id(string value)
    {
        if (value is null || !IdPattern().IsMatch(value)) throw new ArgumentException("Invalid mobile identifier.");
    }
    public static IReadOnlyDictionary<string, MobileNode> Validate(MobilePage page, bool wire = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        Id(page.Id);
        if (page.SchemaVersion != 1 || string.IsNullOrWhiteSpace(page.Title) || page.Title.Length > 200)
            throw new ArgumentException("Unsupported mobile page or title.");
        var nodes = new Dictionary<string, MobileNode>(StringComparer.Ordinal);
        Visit(page.Root, 1);
        foreach (var node in nodes.Values)
        {
            if (wire && node.Action is not null) throw new ArgumentException("RPC definitions cannot cross the mobile wire.");
            if (!wire && node.ActionId is not null) throw new ArgumentException("Action IDs are assigned by the host.");
            if (node.InputIds is null || !wire && node.InputIds.Length != 0 || node.InputIds.Any(id => !nodes.TryGetValue(id, out var input) || input.Type is not ("TextField" or "Toggle")))
                throw new ArgumentException("Invalid wire input IDs.");
            if (node.Type != "Button" && (node.Action is not null || node.ActionId is not null))
                throw new ArgumentException("Only buttons can declare actions.");
            if (node.Action is not { } action) continue;
            if (action.RpcName is null || action.RpcName.Length > 256 || action.RpcName.Split('.').Length < 3 ||
                !Regex.IsMatch(action.RpcName, "^[A-Za-z0-9_.-]+$")) throw new ArgumentException("Use a fully qualified RPC name.");
            if (action.Constants is null || action.Inputs is null || action.Constants.Count + action.Inputs.Count > 32)
                throw new ArgumentException("Invalid action fields.");
            foreach (var field in action.Constants.Keys.Concat(action.Inputs.Keys)) Id(field);
            foreach (var (field, source) in action.Inputs)
            {
                if (action.Constants.ContainsKey(field) || !nodes.TryGetValue(source, out var input) || input.Type is not ("TextField" or "Toggle"))
                    throw new ArgumentException("Invalid or overlapping input mapping.");
            }
        }
        if (JsonSerializer.SerializeToUtf8Bytes(page, MobileJson.Options).Length > MaximumPageBytes)
            throw new ArgumentException("Mobile page exceeds 128 KiB.");
        return nodes;

        void Visit(MobileNode node, int depth)
        {
            if (node is null || depth > 16 || nodes.Count >= 500) throw new ArgumentException("Mobile tree limit exceeded.");
            Id(node.Id);
            if (!Types.Contains(node.Type) || !nodes.TryAdd(node.Id, node) || node.Children is null ||
                node.Text?.Length > 2000 || node.Placeholder?.Length > 2000 || node.MaxLength is < 1 or > 2000 ||
                node.Style is not ("body" or "title" or "caption" or "error")) throw new ArgumentException("Invalid mobile node.");
            if (node.Type == "Scroll" && node.Children.Length != 1 ||
                node.Type is not ("VStack" or "HStack" or "Scroll") && node.Children.Length != 0)
                throw new ArgumentException("Invalid mobile children.");
            foreach (var child in node.Children) Visit(child, depth + 1);
        }
    }
}
