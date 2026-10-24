using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Quantum.Mobile.Internal;

/// <summary>Native-client session state. Persistence delegates must use platform secure storage.</summary>
public sealed class PlatformSession(HttpClient http, Uri platform, Func<Task<string?>> read,
    Func<string, Task> write, Func<Task> clear, TimeProvider? timeProvider = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Uri _platform = PlatformAddress.Parse(platform.AbsoluteUri);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PlatformSessionState? _state;
    private bool _loaded;
    public bool IsAuthenticated => _state?.User is not null && !string.IsNullOrEmpty(_state.AccessToken);
    public PlatformSessionUser? User => _state?.User;
    public string? AccessToken => _state?.AccessToken;
    public event Action? Changed;

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_loaded)
            {
                var saved = await read();
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    try
                    {
                        _state = saved.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<PlatformSessionState>(saved, Json)
                            : new PlatformSessionState { AccessToken = saved, ExpiresAtUtc = LegacyExpiration(saved) };
                    }
                    catch (JsonException) { await ForgetAsync(); }
                }
                _loaded = true;
            }
            if (_state is null) return false;
            await EnsureFreshLockedAsync(null, cancellationToken);
            if (_state!.User is null)
            {
                try { _state.User = await RpcAsync<PlatformSessionUser>("GetCurrentUser", new { }, _state.AccessToken, cancellationToken); }
                catch (PlatformAuthenticationException) { await ForgetAsync(); return false; }
                await SaveAsync(_state);
            }
            return IsAuthenticated;
        }
        catch (PlatformAuthenticationException) { return false; }
        finally { _gate.Release(); }
    }

    public async Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var login = await RpcAsync<PlatformSessionState>("Login", new { email = email.Trim(), password }, null, cancellationToken);
            if (login.User is null || string.IsNullOrEmpty(login.AccessToken)) throw new JsonException("Incomplete login response.");
            SetRefreshTime(login);
            await SaveAsync(login);
            _state = login;
            _loaded = true;
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }

    public async Task<T> ExecuteAsync<T>(Func<string, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(null, cancellationToken);
        try { return await action(access, cancellationToken); }
        catch (PlatformAuthenticationException)
        {
            // Only a definitive authentication rejection can be retried. Never replay
            // a timed-out business write or a response whose outcome is unknown.
            access = await GetAccessAsync(access, cancellationToken);
            try { return await action(access, cancellationToken); }
            catch (PlatformAuthenticationException) { await InvalidateAsync(access); throw; }
        }
    }

    private async Task<string> GetAccessAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureFreshLockedAsync(rejectedToken, cancellationToken);
            return _state!.AccessToken;
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureFreshLockedAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        if (_state is null) throw new PlatformAuthenticationException();
        var now = _clock.GetUtcNow().UtcDateTime;
        if (_state.PendingRefreshId is null && (_state.RefreshAfterUtc ?? _state.ExpiresAtUtc.AddMinutes(-2)) > now &&
            (rejectedToken is null || rejectedToken != _state.AccessToken)) return;
        if (string.IsNullOrEmpty(_state.RefreshToken) || _state.RefreshExpiresAtUtc is not { } refreshExpiry || refreshExpiry <= now)
        {
            await ForgetAsync();
            throw new PlatformAuthenticationException();
        }
        // Persist the idempotency key BEFORE sending. A dropped refresh response or
        // process restart then retries the same rotation, rather than revoking it.
        if (_state.PendingRefreshId is null)
        {
            _state.PendingRefreshId = Guid.NewGuid();
        }
        await SaveAsync(_state);
        PlatformSessionState next;
        try
        {
            next = await RpcAsync<PlatformSessionState>("RefreshLogin", new
            { refreshToken = _state.RefreshToken, requestId = _state.PendingRefreshId }, null, cancellationToken);
        }
        catch (PlatformAuthenticationException) { await ForgetAsync(); throw; }
        if (next.User is null || string.IsNullOrEmpty(next.RefreshToken) || string.IsNullOrEmpty(next.AccessToken))
            throw new JsonException("Incomplete refresh response.");
        SetRefreshTime(next);
        await SaveAsync(next);
        _state = next;
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var token = _state?.RefreshToken;
            await ForgetAsync();
            if (token is null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await RpcAsync<JsonElement>("Logout", new { refreshToken = token }, null, timeout.Token); }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or PlatformRpcException) { }
        }
        finally { _gate.Release(); }
    }

    private async Task InvalidateAsync(string rejectedToken)
    {
        await _gate.WaitAsync();
        try { if (_state?.AccessToken == rejectedToken) await ForgetAsync(); }
        finally { _gate.Release(); }
    }

    private async Task ForgetAsync()
    {
        _state = null;
        _loaded = true;
        Changed?.Invoke();
        await clear();
    }
    private Task SaveAsync(PlatformSessionState state) => write(JsonSerializer.Serialize(state, Json));

    private void SetRefreshTime(PlatformSessionState state)
    {
        var lifetime = state.ExpiresAtUtc - _clock.GetUtcNow().UtcDateTime;
        if (lifetime <= TimeSpan.Zero) throw new JsonException("The platform returned an expired access token.");
        state.RefreshAfterUtc = state.ExpiresAtUtc - TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(2).Ticks, lifetime.Ticks / 10));
    }

    private async Task<T> RpcAsync<T>(string method, object parameters, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_platform, "rpc"))
        { Content = JsonContent.Create(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString("N"), method, @params = parameters }, options: Json) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new PlatformAuthenticationException();
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new PlatformRpcException("rpc_error", "Platform authentication service unavailable.");
        var result = root.GetProperty("result");
        if (!result.GetProperty("isSuccess").GetBoolean())
        {
            var code = result.GetProperty("errorCode").GetString() ?? "login_failed";
            if (code is "authentication_required" or "invalid_refresh_token" or "user_not_found") throw new PlatformAuthenticationException();
            throw new PlatformRpcException(code, result.GetProperty("message").GetString() ?? "Platform request failed.");
        }
        if (!result.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (typeof(T) == typeof(JsonElement)) return (T)(object)JsonSerializer.SerializeToElement(new { });
            throw new JsonException("Missing authentication response.");
        }
        return value.Deserialize<T>(Json) ?? throw new JsonException("Empty authentication response.");
    }

    private static DateTime LegacyExpiration(string token)
    {
        try
        {
            var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight((part.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(part));
            return DateTimeOffset.FromUnixTimeSeconds(json.RootElement.GetProperty("exp").GetInt64()).UtcDateTime;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or FormatException or JsonException or ArgumentOutOfRangeException or KeyNotFoundException)
        { return DateTime.MinValue; }
    }
}

public sealed class PlatformSessionState
{
    public string AccessToken { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RefreshAfterUtc { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshExpiresAtUtc { get; set; }
    public PlatformSessionUser? User { get; set; }
    public Guid? PendingRefreshId { get; set; }
    public override string ToString() => "[REDACTED]";
}

public sealed class PlatformSessionUser
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public int Roles { get; set; }
}

public class PlatformRpcException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class PlatformAuthenticationException() : PlatformRpcException("authentication_required", "Your login has expired. Sign in again.");
