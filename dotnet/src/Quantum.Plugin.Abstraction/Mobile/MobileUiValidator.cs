using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quantum.Plugin.Abstraction;

public static partial class MobileUiValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public const int MaximumPageBytes = 128 * 1024;
    private static readonly HashSet<string> Types = ["VStack", "HStack", "Scroll", "Text", "TextField", "Toggle", "Button", "Divider", "Spacer", "Progress"];
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    public static void Id(string value)
    {
        if (value is null || !IdPattern().IsMatch(value)) throw new ArgumentException("Invalid mobile identifier.");
    }
    public static IReadOnlyDictionary<string, MobileNode> Validate(MobilePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Id(page.Id);
        if (page.SchemaVersion != 1 || string.IsNullOrWhiteSpace(page.Title) || page.Title.Length > 200)
            throw new ArgumentException("Unsupported mobile page or title.");
        var nodes = new Dictionary<string, MobileNode>(StringComparer.Ordinal);
        Visit(page.Root, 1);
        foreach (var node in nodes.Values)
        {
            if (node.Type != "Button" && node.Action is not null)
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
        if (JsonSerializer.SerializeToUtf8Bytes(page, JsonOptions).Length > MaximumPageBytes)
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
