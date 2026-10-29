using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NOF.Application;
using NOF.Domain;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using Quantum.Mobile.Internal;
using Quantum.Platform.Application;
using Quantum.Platform.Application.Handlers;
using Quantum.Platform.Authentication;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;
using static Quantum.Platform.Tests.PasswordResetTests;

namespace Quantum.Platform.Tests;

public sealed class PersistentLoginHttpTests
{
    [Fact]
    public async Task RealHttpPipelineRefreshesTheBridgeRestoresLoginAndRevokesOnLogout()
    {
        var clock = new Clock();
        var users = new MemoryRepository<PlatformUser>();
        var sessions = new MemoryRepository<PlatformLoginSession>();
        var password = new Pbkdf2PlatformPasswordHasher();
        users.Add(PlatformUser.Create("native-test", "native@example.test", password.Hash("fixture-password-123"), idGenerator: new SnowflakeIdGenerator()));
        users.Add(PlatformUser.Create("other-native", "other@example.test", password.Hash("fixture-password-456"), idGenerator: new SnowflakeIdGenerator()));
        var jwt = new PlatformJwtOptions { SigningKey = new string('k', 64) };
        var builder = NOFWebApplicationBuilder.Create([]);
        builder.WebApplicationBuilder.WebHost.UseTestServer();
        builder.AddApplicationPart(typeof(IQuantumPlatformService).Assembly);
        builder.AddApplicationPart(typeof(Login).Assembly);
        builder.AddRpcServer<QuantumPlatformService>();
        builder.AddRpcServer<MobileBridgeService>();
        builder.Services.AddQuantumPlatformApplication();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IRepository<PlatformUser>>(users);
        builder.Services.AddSingleton<IRepository<PlatformLoginSession>>(sessions);
        builder.Services.AddSingleton<IRepository<MobileBridgeAccount>>(new MemoryRepository<MobileBridgeAccount>());
        builder.Services.AddSingleton<IRepository<AuditEntry>>(new MemoryRepository<AuditEntry>());
        builder.Services.AddSingleton<IDbContext>(DispatchProxy.Create<IDbContext, SaveOnlyContext>());
        builder.Services.AddSingleton<IPlatformPasswordHasher>(password);
        builder.Services.AddSingleton<IRegistrationLock, Gate>();
        builder.Services.AddSingleton<IMobileAttachmentStore, MemoryAttachments>();
        builder.Services.AddSingleton<IOptions<PlatformJwtOptions>>(Options.Create(jwt));
        builder.Services.AddSingleton<IRefreshTokenCodec, RefreshTokenCodec>();
        builder.Services.AddSingleton<IPlatformTokenIssuer, JwtPlatformTokenIssuer>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IPlatformCallerContext, HttpPlatformCallerContext>();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                LifetimeValidator = (notBefore, expires, _, _) => notBefore <= clock.GetUtcNow().UtcDateTime && expires > clock.GetUtcNow().UtcDateTime
            };
        });
        await using var app = await builder.BuildAsync();
        app.UseAuthentication();
        await app.StartAsync();
        using var http = app.GetTestClient();
        http.BaseAddress = new Uri("http://localhost/");
        string? saved = null;
        PlatformSession NewSession() => new(http, http.BaseAddress, () => Task.FromResult(saved),
            value => { saved = value; return Task.CompletedTask; }, () => { saved = null; return Task.CompletedTask; }, clock);
        var authentication = NewSession();
        await authentication.LoginAsync("native@example.test", "fixture-password-123");
        var accessBefore = authentication.AccessToken;
        var bridge = new BridgeClient(http, http.BaseAddress, () => authentication.AccessToken) { Authentication = authentication };
        bridge.Registration = await bridge.CallAsync<BridgeRegistration>("register", new { name = "native-test-phone", kind = "mobile" });
        var desktop = new BridgeClient(http, http.BaseAddress, () => authentication.AccessToken) { Authentication = authentication };
        desktop.Registration = await desktop.CallAsync<BridgeRegistration>("register", new { name = "native-desktop", kind = "desktop" });
        Assert.Empty(await bridge.CallAsync<BridgePageInfo[]>("pages", new { desktopId = desktop.Registration.Device.Id }));
        var other = new PlatformSession(http, http.BaseAddress, () => Task.FromResult<string?>(null), _ => Task.CompletedTask, () => Task.CompletedTask, clock);
        await other.LoginAsync("other@example.test", "fixture-password-456");
        var foreign = new BridgeClient(http, http.BaseAddress, () => other.AccessToken) { Authentication = other };
        foreign.Registration = await foreign.CallAsync<BridgeRegistration>("register", new { name = "foreign-phone", kind = "mobile" });
        Assert.DoesNotContain(await foreign.CallAsync<BridgeDevice[]>("devices"), d => d.Id == desktop.Registration.Device.Id);
        Assert.Equal("mobile_not_found", (await Assert.ThrowsAsync<BridgeException>(() => foreign.CallAsync<BridgePageInfo[]>("pages", new { desktopId = desktop.Registration.Device.Id }))).Code);
        // The same authenticated NOF HTTP route carries bounded chunks; queued commands contain references only.
        await desktop.CallAsync<JsonElement>("catalog", new { pages = new[] { new BridgePageInfo("plugin.chat", "chat", "Chat") } });
        var page = new MobilePage("chat", "Chat", new MobileNode("VStack", "root") { Children = [new("Attachments", "files"), new("Button", "send") { Text = "Send", ActionId = "media-action", InputIds = ["files"] }] }) { SchemaVersion = 2 };
        var view = new BridgeSnapshot("media-view", desktop.Registration.Device.SessionId, "plugin.chat", "runtime", clock.GetUtcNow().AddMinutes(5), page);
        await desktop.CallAsync<JsonElement>("send", new BridgeSend("frame", bridge.Registration.Device.Id, "snapshot", MobileJson.Element(view)));
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(MobileAttachments.ChunkBytes + 7);
        var file = new BridgeAttachment(Guid.NewGuid().ToString("N"), "selected.bin", "application/octet-stream", bytes.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        var create = new { desktopId = desktop.Registration.Device.Id, viewId = view.ViewId, inputId = "files", attachment = file };
        await bridge.CallAsync<JsonElement>("attachment/create", create);
        for (var offset = 0; offset < bytes.Length; offset += MobileAttachments.ChunkBytes)
        {
            var chunk = bytes.Skip(offset).Take(MobileAttachments.ChunkBytes).ToArray();
            await bridge.CallAsync<JsonElement>("attachment/write", new { attachmentId = file.Id, offset, dataBase64 = Convert.ToBase64String(chunk) });
        }
        await bridge.CallAsync<JsonElement>("attachment/complete", new { attachmentId = file.Id });
        Assert.True((await bridge.CallAsync<JsonElement>("attachment/create", create)).GetProperty("ready").GetBoolean());
        var downloaded = await desktop.CallAsync<JsonElement>("attachment/read", new { attachmentId = file.Id, offset = MobileAttachments.ChunkBytes });
        Assert.Equal(bytes[MobileAttachments.ChunkBytes..], Convert.FromBase64String(downloaded.GetProperty("dataBase64").GetString()!));
        Assert.Equal("mobile_not_found", (await Assert.ThrowsAsync<BridgeException>(() => foreign.CallAsync<JsonElement>("attachment/read", new { attachmentId = file.Id, offset = 0 }))).Code);
        await bridge.CallAsync<JsonElement>("send", new BridgeSend("media-command", desktop.Registration.Device.Id, "action", MobileJson.Element(new BridgeInvoke(view.ViewId, "media-action", new() { ["files"] = MobileJson.Element(new[] { file }) }))));
        clock.Advance(TimeSpan.FromMinutes(61));
        await bridge.CallAsync<JsonElement>("heartbeat");
        Assert.NotEqual(accessBefore, authentication.AccessToken);
        Assert.Equal(2, sessions.Count);
        var restored = NewSession();
        Assert.True(await restored.RestoreAsync());
        Assert.Equal(authentication.User!.UserId, restored.User!.UserId);
        var oldAccess = restored.AccessToken;
        await restored.LogoutAsync();
        Assert.Null(saved);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rpc")
        { Content = JsonContent.Create(new { jsonrpc = "2.0", id = "logged-out", method = "GetCurrentUser", @params = new { } }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oldAccess);
        using var response = await http.SendAsync(request);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(result.RootElement.GetProperty("result").GetProperty("isSuccess").GetBoolean());
        Assert.Equal("authentication_required", result.RootElement.GetProperty("result").GetProperty("errorCode").GetString());
        await app.StopAsync();
    }

    private sealed class MemoryAttachments : IMobileAttachmentStore
    {
        private readonly Dictionary<string, byte[]> _files = [];
        public Task WriteAsync(string account, BridgeAttachment file, long offset, byte[] data, CancellationToken token)
        {
            var key = account + file.Id;
            if (!_files.TryGetValue(key, out var bytes)) _files[key] = bytes = new byte[(int)file.Size];
            data.CopyTo(bytes, (int)offset);
            return Task.CompletedTask;
        }
        public Task CompleteAsync(string account, BridgeAttachment file, CancellationToken token)
        {
            Assert.Equal(file.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_files[account + file.Id])));
            return Task.CompletedTask;
        }
        public Task<byte[]> ReadAsync(string account, BridgeAttachment file, long offset, CancellationToken token)
            => Task.FromResult(_files[account + file.Id].Skip((int)offset).Take(MobileAttachments.ChunkBytes).ToArray());
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
    private sealed class Gate : IRegistrationLock
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        public async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken cancellationToken)
        { await _gate.WaitAsync(cancellationToken); return new Lease(_gate); }
        private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
        { public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; } }
    }
}
