namespace Quantum.MobileApp.Debugging;

public sealed class MobileDebugOptions
{
    public required int Port { get; set; }
    public required string Token { get; set; }

    public static MobileDebugOptions? FromEnvironment()
    {
        var port = Environment.GetEnvironmentVariable("QUANTUM_MOBILE_DEBUG_PORT");
        if (string.IsNullOrEmpty(port)) return null;
        if (!int.TryParse(port, out var number)) throw new ArgumentException("Invalid mobile debug port.");
        var options = new MobileDebugOptions { Port = number, Token = Environment.GetEnvironmentVariable("QUANTUM_MOBILE_DEBUG_TOKEN") ?? "" };
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (Port is < 1024 or > 65535) throw new ArgumentException("The debug port must be between 1024 and 65535.");
        if (string.IsNullOrEmpty(Token) || Token.Length is < 32 or > 256 || Token.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("A debug token of 32–256 non-whitespace ASCII characters is required.");
    }

    public override string ToString() => "[REDACTED]";
}

public sealed class MobileDebugException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public MobileDebugException(int status, string code) : base(code) { Status = status; Code = code; }
}

public interface IMobileDebugTarget
{
    Task<object> ExecuteAsync(string path, System.Text.Json.JsonElement body);
}

public sealed class DebugDesktopRequest
{
    public required string DesktopId { get; set; }
}

public sealed class DebugOpenRequest
{
    public required string PluginId { get; set; }
    public required string PageId { get; set; }
    public bool DiscardDraft { get; set; }
}

public class DebugViewRequest
{
    public required string ViewId { get; set; }
    public bool DiscardDraft { get; set; }
}

public sealed class DebugInputRequest : DebugViewRequest
{
    public required string NodeId { get; set; }
    public required System.Text.Json.JsonElement Value { get; set; }
}

public sealed class DebugPressRequest : DebugViewRequest
{
    public required string NodeId { get; set; }
}
