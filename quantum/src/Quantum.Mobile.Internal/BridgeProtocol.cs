using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Quantum.Mobile.Internal;

public sealed record BridgeRequest(string Operation, string? DeviceId = null, string? DeviceSecret = null, JsonElement Body = default);
public sealed record BridgeDevice(string Id, string Name, string Kind, DateTimeOffset SeenAt, string SessionId);
public sealed record BridgeRegistration(BridgeDevice Device, string Secret);
public sealed record BridgePageInfo(string PluginId, string PageId, string Title);
public sealed record BridgeBinding(string DesktopId, string MobileId, string MobileName, bool Approved);
public sealed record BridgeMessage(string Id, string SenderId, string TargetId, string Kind, JsonElement Body,
    DateTimeOffset ExpiresAt, string? Lease = null);
public sealed record BridgeSnapshot(string ViewId, string DesktopSessionId, string PluginId, string RuntimeId,
    DateTimeOffset ExpiresAt, MobilePage Page, string? RequestId = null);
public sealed record BridgeOpen(string PluginId, string PageId);
public sealed record BridgePageError(string RequestId, string Code, string Message);
public sealed record BridgeInvoke(string ViewId, string ActionId, Dictionary<string, JsonElement> Inputs);
public sealed record BridgeOutcome(string RequestId, string Status, string Message, JsonElement? Result = null);
public sealed record BridgeSend(string Id, string TargetId, string Kind, JsonElement Body);
public sealed record BridgeAck(string Id, string Lease);

/// <summary>Only platform control RPC is available here. This client cannot invoke plugin RPC names.</summary>
public sealed class BridgeClient(HttpClient http, Uri platform, Func<string?> accessToken)
{
    public BridgeRegistration? Registration { get; set; }
    private TimeSpan _serverOffset;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + _serverOffset;
    public async Task<T> CallAsync<T>(string operation, object? body = null, CancellationToken cancellationToken = default)
    {
        var token = accessToken();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Sign in to Quantum Platform first.");
        if (platform.Scheme != "https" && !(platform.IsLoopback && platform.Scheme == "http"))
            throw new ArgumentException("HTTPS is required outside localhost.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(platform, "rpc/mobile"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method = "ExchangeMobileBridge",
            @params = new BridgeRequest(operation, Registration?.Device.Id, Registration?.Secret, MobileJson.Element(body ?? new { }))
        });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Headers.Date is { } serverTime) _serverOffset = serverTime - DateTimeOffset.UtcNow;
        await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var rpcError) && rpcError.ValueKind != JsonValueKind.Null) throw new InvalidOperationException("Platform bridge RPC is unavailable.");
        var result = root.GetProperty("result");
        if (!result.GetProperty("isSuccess").GetBoolean())
            throw new BridgeException(result.GetProperty("errorCode").GetString() ?? "mobile_failed", result.GetProperty("message").GetString() ?? "Bridge request failed.");
        return MobileJson.Read<T>(result.GetProperty("value"));
    }
}

public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
