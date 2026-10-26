using MobileUiValidator = Quantum.Mobile.Internal.MobileUiValidator;
using System.Text.Json;
using Quantum.Marketplace;
using Quantum.Mobile.Internal;

namespace Quantum.Mobile;

/// <summary>Same-account bridge. Its journal precedes acknowledgement and business execution.</summary>
public sealed class MobileDesktopBridge(
    HttpClient http, QuantumMarketplaceSession session, QuantumMarketplaceOptions options,
    MobileActionGateway gateway, string journalPath,
    Func<string, Task<string?>> readRegistration, Func<string, string, Task> saveRegistration, Action<string> clearRegistration) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1);
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private BridgeClient? _client;
    private string? _catalogSignature;
    public bool IsRunning => _loop is { IsCompleted: false };
    public string Status { get; private set; } = "Stopped";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private Task? _automatic;
    private string? _activeUser;
    public event Action? Changed;

    public void Initialize(IQuantumMarketplaceClient marketplace)
    {
        if (_automatic is not null) return;
        session.Changed += SessionChanged;
        _automatic = Task.Run(() => RunAutomaticAsync(marketplace, _lifetime.Token));
    }

    private void SessionChanged()
    {
        if (_activeUser is not null && _activeUser != session.User?.UserId)
        {
            gateway.SetSharing([]);
            _cancellation?.Cancel();
        }
        try { _wake.Release(); } catch (Exception e) when (e is SemaphoreFullException or ObjectDisposedException) { }
        Changed?.Invoke();
    }

    private async Task RunAutomaticAsync(IQuantumMarketplaceClient marketplace, CancellationToken token)
    {
        var delay = 2;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await session.InitializeAsync(marketplace, token);
                if (!session.IsAuthenticated || _activeUser is not null && _activeUser != session.User?.UserId)
                {
                    if (_client is not null || _activeUser is not null) await StopAsync();
                }
                if (session.IsAuthenticated && !IsRunning) await StartAsync(token);
                delay = 2;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                gateway.SetSharing([]);
                Status = "Connection interrupted"; Changed?.Invoke(); delay = Math.Min(delay * 2, 60);
            }
            try { await _wake.WaitAsync(TimeSpan.FromSeconds(delay), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;
            if (!session.IsAuthenticated) throw new InvalidOperationException("Sign in to Quantum Platform first.");
            _activeUser = session.User!.UserId;
            _client = new(http, options.BaseAddress, () => session.AccessToken) { Authentication = session.Authentication };
            var saved = await readRegistration(_activeUser);
            if (saved is not null)
            {
                _client.Registration = JsonSerializer.Deserialize<BridgeRegistration>(saved, MobileJson.Options);
                try { await _client.CallAsync<JsonElement>("heartbeat", cancellationToken: cancellationToken); }
                catch (BridgeException exception) when (exception.Code == "mobile_device_revoked") { _client.Registration = null; }
            }
            if (_client.Registration is null)
            {
                _client.Registration = await _client.CallAsync<BridgeRegistration>("register", new { name = Environment.MachineName, kind = "desktop" }, cancellationToken);
                await saveRegistration(_activeUser, JsonSerializer.Serialize(_client.Registration, MobileJson.Options));
            }
            Directory.CreateDirectory(journalPath);
            var pages = gateway.ShareAllPages();
            await _client.CallAsync<JsonElement>("catalog", new { pages }, cancellationToken);
            _catalogSignature = JsonSerializer.Serialize(pages, MobileJson.Options);
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var client = _client;
            var loopToken = _cancellation.Token;
            var user = _activeUser;
            _loop = Task.Run(() => RunAsync(client, user, loopToken), CancellationToken.None);
        }
        catch { gateway.SetSharing([]); throw; }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            // Close the execution entrance before attempting network revocation.
            gateway.SetSharing([]);
            _cancellation?.Cancel();
            if (_loop is not null) await _loop;
            if (_client?.Registration is not null)
            {
                try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await _client.CallAsync<JsonElement>("revoke", cancellationToken: timeout.Token); }
                catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or BridgeException or PlatformAuthenticationException or InvalidOperationException) { }
            }
            if (_activeUser is { } user) clearRegistration(user);
            _client = null; _activeUser = null; Status = "Stopped"; Changed?.Invoke();
        }
        finally { _lifecycle.Release(); }
    }

    private async Task RunAsync(BridgeClient client, string user, CancellationToken token)
    {
        var delay = 2;
        while (!token.IsCancellationRequested && session.IsAuthenticated && session.User?.UserId == user)
        {
            try
            {
                var pages = gateway.ShareAllPages();
                var signature = JsonSerializer.Serialize(pages, MobileJson.Options);
                if (signature != _catalogSignature)
                {
                    await client.CallAsync<JsonElement>("catalog", new { pages }, token);
                    _catalogSignature = signature;
                }
                await FlushJournalAsync(client, token);
                var messages = await client.CallAsync<BridgeMessage[]>("poll", cancellationToken: token);
                foreach (var message in messages)
                {
                    token.ThrowIfCancellationRequested();
                    if (message.Kind == "open")
                    {
                        try
                        {
                            var snapshot = gateway.Open(message.SenderId, client.Registration!.Device.SessionId,
                                MobileJson.Read<BridgeOpen>(message.Body), client.UtcNow) with
                            { RequestId = message.Id };
                            await SendAsync(client, new(Guid.NewGuid().ToString("N"), message.SenderId, "snapshot", MobileJson.Element(snapshot)), token);
                        }
                        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or BridgeException)
                        {
                            var code = exception is BridgeException bridge ? bridge.Code : "mobile_page_unavailable";
                            await SendAsync(client, new(Guid.NewGuid().ToString("N"), message.SenderId, "pageError",
                                MobileJson.Element(new BridgePageError(message.Id, code, "Page could not be generated. Check the desktop plugin and refresh."))), token);
                        }
                        await AckAsync(client, message, token);
                    }
                    else if (message.Kind == "action") await ProcessActionAsync(client, message, token);
                }
                Status = "Connected"; delay = 2; Changed?.Invoke();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (PlatformAuthenticationException)
            {
                break;
            }
            catch (BridgeException exception) when (exception.Code == "mobile_device_revoked")
            {
                clearRegistration(user);
                _client = null;
                break;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Status = exception is BridgeException bridge ? bridge.Code : "Connection interrupted";
                Changed?.Invoke(); delay = Math.Min(delay * 2, 60);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(delay + Random.Shared.NextDouble() * .4), token); }
            catch (OperationCanceledException) { break; }
        }
        gateway.SetSharing([]);
        Status = "Stopped"; Changed?.Invoke();
    }

    private async Task ProcessActionAsync(BridgeClient client, BridgeMessage message, CancellationToken token)
    {
        var file = JournalFile(client, message.Id);
        if (File.Exists(file)) { await AckAsync(client, message, token); return; }
        var entry = new JournalEntry(client.Registration!.Device.Id, message.SenderId, message.Id, DateTimeOffset.UtcNow, null);
        await SaveAsync(file, entry, token); // Durable claim precedes Ack and business execution.
        await AckAsync(client, message, token);
        BridgeOutcome outcome;
        try
        {
            if (message.ExpiresAt <= client.UtcNow) throw new BridgeException("mobile_action_expired", "Action expired.");
            await client.CallAsync<JsonElement>("authorize", new { requestId = message.Id }, token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            outcome = await gateway.InvokeAsync(message.Id, message.SenderId, MobileJson.Read<BridgeInvoke>(message.Body), timeout.Token)
                .WaitAsync(timeout.Token);
            if (JsonSerializer.SerializeToUtf8Bytes(outcome, MobileJson.Options).Length > 120 * 1024)
                outcome = new(message.Id, "unknown", "The operation returned an oversized result. Check desktop state before retrying.");
        }
        catch (BridgeException exception) { outcome = new(message.Id, "failed", exception.Code); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { outcome = new(message.Id, "unknown", "The result is unknown. Check desktop state before retrying."); }
        await SaveAsync(file, entry with { Outcome = outcome }, CancellationToken.None);
    }

    private async Task FlushJournalAsync(BridgeClient client, CancellationToken token)
    {
        foreach (var file in Directory.EnumerateFiles(journalPath, client.Registration!.Device.Id + "-*.json"))
        {
            var entry = JsonSerializer.Deserialize<JournalEntry>(await File.ReadAllTextAsync(file, token), MobileJson.Options)!;
            if (entry.CreatedAt < DateTimeOffset.UtcNow.AddHours(-24)) { File.Delete(file); continue; }
            if (entry.Uploaded) continue;
            var outcome = entry.Outcome ?? new(entry.RequestId, "unknown", "Desktop restarted during execution. Check the result before retrying.");
            if (entry.Outcome is null) await SaveAsync(file, entry with { Outcome = outcome }, token);
            // Stable transport ID makes retries harmless, even if the prior upload response was lost.
            try
            {
                await SendAsync(client, new(entry.RequestId + "-result", entry.MobileId, "result", MobileJson.Element(outcome)), token);
                await SaveAsync(file, entry with { Outcome = outcome, Uploaded = true }, token);
            }
            catch (BridgeException exception) when (exception.Code is "mobile_not_found" or "mobile_request_conflict")
            {
                await SaveAsync(file, entry with { Outcome = outcome, Uploaded = true }, token);
            }
        }
    }
    private string JournalFile(BridgeClient client, string id)
    {
        MobileUiValidator.Id(id);
        return Path.Combine(journalPath, client.Registration!.Device.Id + "-" + id + ".json");
    }
    private static async Task SaveAsync(string file, JournalEntry entry, CancellationToken token)
    {
        var temporary = file + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entry, MobileJson.Options), token);
        File.Move(temporary, file, overwrite: true);
    }
    private static Task<JsonElement> AckAsync(BridgeClient client, BridgeMessage message, CancellationToken token)
        => client.CallAsync<JsonElement>("ack", new BridgeAck(message.Id, message.Lease!), token);
    private static Task<JsonElement> SendAsync(BridgeClient client, BridgeSend message, CancellationToken token)
        => client.CallAsync<JsonElement>("send", message, token);
    public async ValueTask DisposeAsync()
    {
        session.Changed -= SessionChanged;
        await _lifetime.CancelAsync();
        if (_automatic is not null) await _automatic;
        await StopAsync();
        _cancellation?.Dispose(); _lifecycle.Dispose(); _lifetime.Dispose(); _wake.Dispose();
    }
    private sealed record JournalEntry(string DesktopId, string MobileId, string RequestId, DateTimeOffset CreatedAt, BridgeOutcome? Outcome, bool Uploaded = false);
}
