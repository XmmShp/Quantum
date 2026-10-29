using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Quantum.Mobile.Internal;

namespace Quantum.Tests;

public sealed class PlatformSessionTests
{
    private static readonly Uri Platform = new("https://platform.example/");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Clock _clock = new();
    private string? _saved;
    private PlatformSession NewSession(HttpClient http) => new(http, Platform,
        () => Task.FromResult(_saved), text => { _saved = text; return Task.CompletedTask; }, () => { _saved = null; return Task.CompletedTask; }, _clock);
    private PlatformSessionState State(string access = "old-access") => new()
    {
        AccessToken = access, ExpiresAtUtc = _clock.GetUtcNow().UtcDateTime.AddHours(1),
        RefreshToken = "fixture-refresh", RefreshExpiresAtUtc = _clock.GetUtcNow().UtcDateTime.AddDays(30),
        User = new() { UserId = "1", Username = "tester", Email = "tester@example.test", Roles = 1 }
    };
    private static HttpResponseMessage Success(object state) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new { jsonrpc = "2.0", result = new { isSuccess = true, value = state } }) };
    private static HttpResponseMessage Invalid() => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new { jsonrpc = "2.0", result = new { isSuccess = false, errorCode = "invalid_refresh_token", message = "Expired." } }) };

    [Fact]
    public async Task ConcurrentExpiredRequestsUseOneRefreshAndPersistTheRotation()
    {
        var initial = State(); _saved = JsonSerializer.Serialize(initial, Json);
        var calls = 0;
        using var http = new HttpClient(new Handler(async (_, _) =>
        {
            Interlocked.Increment(ref calls); await Task.Delay(20); return Success(State("fresh-access"));
        }));
        var session = NewSession(http);
        Assert.True(await session.RestoreAsync());
        _clock.Advance(TimeSpan.FromHours(1));
        var values = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => session.ExecuteAsync((token, _) => Task.FromResult(token))));
        Assert.All(values, value => Assert.Equal("fresh-access", value));
        Assert.Equal(1, calls);
        Assert.Equal("fresh-access", JsonSerializer.Deserialize<PlatformSessionState>(_saved!, Json)!.AccessToken);
    }

    [Fact]
    public async Task LostRefreshResponseSurvivesRestartWithTheSameRequestId()
    {
        var initial = State(); _saved = JsonSerializer.Serialize(initial, Json);
        Guid? requestId = null;
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            using var json = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync(ct), cancellationToken: ct);
            var parameters = json.RootElement.GetProperty("params");
            Assert.Equal("fixture-refresh", parameters.GetProperty("refreshToken").GetString());
            var id = parameters.GetProperty("requestId").GetGuid();
            if (calls++ == 0) { requestId = id; throw new HttpRequestException("Response was lost."); }
            Assert.Equal(requestId, id);
            return Success(State("recovered-access"));
        }));
        var first = NewSession(http); await first.RestoreAsync();
        _clock.Advance(TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<HttpRequestException>(() => first.ExecuteAsync((token, _) => Task.FromResult(token)));
        Assert.NotNull(JsonSerializer.Deserialize<PlatformSessionState>(_saved!, Json)!.PendingRefreshId);
        var restarted = NewSession(http);
        Assert.True(await restarted.RestoreAsync());
        Assert.Equal("recovered-access", restarted.AccessToken);
        Assert.Null(JsonSerializer.Deserialize<PlatformSessionState>(_saved!, Json)!.PendingRefreshId);
    }

    [Fact]
    public async Task OfflineRestoreRetainsCredentialsAndCanRecover()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        _clock.Advance(TimeSpan.FromHours(1));
        var offline = true;
        using var http = new HttpClient(new Handler((_, _) => offline ? throw new HttpRequestException("Offline") : Task.FromResult(Success(State("online-access")))));
        var session = NewSession(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => session.RestoreAsync());
        Assert.NotNull(_saved);
        offline = false;
        Assert.True(await session.RestoreAsync());
        Assert.Equal("online-access", session.AccessToken);
    }

    [Fact]
    public async Task RevokedRefreshClearsMemoryAndSecureStorage()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        _clock.Advance(TimeSpan.FromHours(1));
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Invalid())));
        var session = NewSession(http);
        Assert.False(await session.RestoreAsync());
        Assert.False(session.IsAuthenticated);
        Assert.Null(_saved);
    }

    [Fact]
    public async Task BusinessTimeoutIsNeverReplayed()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected auth request")));
        var session = NewSession(http); await session.RestoreAsync();
        var attempts = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() => session.ExecuteAsync<int>((_, _) =>
        { attempts++; throw new HttpRequestException("Unknown write outcome"); }));
        Assert.Equal(1, attempts);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task AuthenticationRejectionRefreshesAndRetriesExactlyOnce()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        var refreshes = 0;
        using var http = new HttpClient(new Handler((_, _) => { refreshes++; return Task.FromResult(Success(State("fresh-access"))); }));
        var session = NewSession(http); await session.RestoreAsync();
        var attempts = 0;
        var value = await session.ExecuteAsync((token, _) =>
        { attempts++; if (token == "old-access") throw new PlatformAuthenticationException(); return Task.FromResult(42); });
        Assert.Equal(42, value); Assert.Equal(2, attempts); Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task LogoutCannotBeUndoneByAnInFlightRefresh()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            var text = await request.Content!.ReadAsStringAsync(ct);
            if (text.Contains("RefreshLogin")) { entered.SetResult(); await finish.Task; return Success(State("fresh-access")); }
            Assert.Contains("Logout", text); return Success(new { });
        }));
        var session = NewSession(http); await session.RestoreAsync(); _clock.Advance(TimeSpan.FromHours(1));
        var refreshing = session.ExecuteAsync((token, _) => Task.FromResult(token));
        await entered.Task;
        var logout = session.LogoutAsync(); finish.SetResult();
        await Task.WhenAll(refreshing, logout);
        Assert.False(session.IsAuthenticated); Assert.Null(_saved);
    }

    [Fact]
    public async Task MobileBridgeUsesTheSameRefreshAndRetainsDeviceRegistration()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        var refreshes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/rpc") { refreshes++; return Task.FromResult(Success(State("fresh-access"))); }
            Assert.Equal("fresh-access", request.Headers.Authorization?.Parameter);
            return Task.FromResult(Success(Array.Empty<BridgeDevice>()));
        }));
        var session = NewSession(http); await session.RestoreAsync(); _clock.Advance(TimeSpan.FromHours(1));
        var bridge = new BridgeClient(http, Platform, () => session.AccessToken) { Authentication = session,
            Registration = new(new("fixture-device", "Phone", "mobile", _clock.GetUtcNow(), "fixture-session"), "fixture-secret") };
        Assert.Empty(await bridge.CallAsync<BridgeDevice[]>("devices"));
        Assert.Equal(1, refreshes); Assert.Equal("fixture-device", bridge.Registration!.Device.Id);
    }

    [Fact]
    public async Task RefreshDeadlineExpiresLocallyWithoutNetwork()
    {
        _saved = JsonSerializer.Serialize(State(), Json); _clock.Advance(TimeSpan.FromDays(30));
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected network")));
        Assert.False(await NewSession(http).RestoreAsync()); Assert.Null(_saved);
    }

    [Fact]
    public async Task ShortLivedAccessTokensDoNotRefreshOnEveryRequest()
    {
        var state = State(); state.ExpiresAtUtc = _clock.GetUtcNow().UtcDateTime.AddMinutes(5);
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Success(state)); }));
        var session = NewSession(http);
        await session.LoginAsync("tester@example.test", "never-store-this-password");
        for (var i = 0; i < 10; i++) await session.ExecuteAsync((token, _) => Task.FromResult(token));
        Assert.Equal(1, calls);
        Assert.DoesNotContain("never-store-this-password", _saved);
    }

    [Fact]
    public async Task ASecondAuthenticationRejectionClearsTheLoginWithoutAnInfiniteRetry()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        var refreshes = 0;
        using var http = new HttpClient(new Handler((_, _) => { refreshes++; return Task.FromResult(Success(State("fresh-access"))); }));
        var session = NewSession(http); await session.RestoreAsync();
        var attempts = 0;
        await Assert.ThrowsAsync<PlatformAuthenticationException>(() => session.ExecuteAsync<int>((_, _) => { attempts++; throw new PlatformAuthenticationException(); }));
        Assert.Equal(2, attempts); Assert.Equal(1, refreshes); Assert.Null(_saved);
    }

    [Fact]
    public async Task CancellationAndRefreshNetworkFailureDoNotEraseTheSavedLogin()
    {
        _saved = JsonSerializer.Serialize(State(), Json);
        using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("Offline")));
        var session = NewSession(http); await session.RestoreAsync();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ExecuteAsync((token, _) => Task.FromResult(token), canceled.Token));
        Assert.NotNull(_saved);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
}
