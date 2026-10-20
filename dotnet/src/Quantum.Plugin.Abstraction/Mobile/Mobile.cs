namespace Quantum.Plugin.Abstraction;

public static class Mobile
{
    public static MobilePage Page(string id, string title, MobileNode root) => new() { Id = id, Title = title, Root = root };
    public static MobileNode VStack(string id, params MobileNode[] children) => new() { Type = "VStack", Id = id, Children = children };
    public static MobileNode HStack(string id, params MobileNode[] children) => new() { Type = "HStack", Id = id, Children = children };
    public static MobileNode Scroll(string id, MobileNode child) => new() { Type = "Scroll", Id = id, Children = [child] };
    public static MobileNode Text(string id, string text) => new() { Type = "Text", Id = id, Text = text };
    public static MobileNode TextField(string id) => new() { Type = "TextField", Id = id };
    public static MobileNode Toggle(string id, string text) => new() { Type = "Toggle", Id = id, Text = text };
    public static MobileNode Button(string id, string text) => new() { Type = "Button", Id = id, Text = text };
    public static MobileNode Divider(string id) => new() { Type = "Divider", Id = id };
    public static MobileNode Spacer(string id) => new() { Type = "Spacer", Id = id };
    public static MobileNode Progress(string id) => new() { Type = "Progress", Id = id };
    public static MobileRpcAction Rpc(string qualifiedName) => new() { RpcName = qualifiedName };
}

