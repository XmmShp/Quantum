using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex.Application;

internal static class CodexErrorMessages
{
    private const string UpgradeRequiredMarker = "requires a newer version of Codex";

    public static string Normalize(string? message)
    {
        var normalized = ExtractNestedMessage(message) ?? message?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "The Codex request failed.";
        }

        return normalized.Contains(UpgradeRequiredMarker, StringComparison.OrdinalIgnoreCase)
            ? "The installed Codex CLI is too old for the selected model. "
                + "Run `npm install -g @openai/codex@latest`, then disconnect and reconnect. "
                + "/ 当前 Codex CLI 版本过旧，请升级后断开并重新连接。"
            : normalized;
    }

    private static string? ExtractNestedMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.TrimStart()[0] != '{')
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var nestedMessage)
                && nestedMessage.ValueKind == JsonValueKind.String)
            {
                return nestedMessage.GetString();
            }

            return root.TryGetProperty("message", out var directMessage)
                && directMessage.ValueKind == JsonValueKind.String
                    ? directMessage.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
