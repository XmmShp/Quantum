using System.Reflection;
using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Domain;
using Quantum.Platform.Application;
using Quantum.Platform.Application.Handlers;
using Quantum.Platform.Authentication;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;
using static Quantum.Platform.Tests.PasswordResetTests;

namespace Quantum.Platform.Tests;

public sealed class LoginSessionTests
{
    private readonly PlatformUser _user = PlatformUser.Create("tester", "tester@example.test", "password-hash", idGenerator: new SnowflakeIdGenerator());
    private readonly MemoryRepository<PlatformUser> _users = new();
    private readonly MemoryRepository<PlatformLoginSession> _sessions = new();
    private readonly Clock _clock = new();
    private readonly RefreshTokenCodec _codec = new(Options.Create(new PlatformJwtOptions { SigningKey = new string('k', 64) }));
    private readonly IDbContext _db = DispatchProxy.Create<IDbContext, SaveOnlyContext>();
    private readonly Issuer _issuer = new();

    private (PlatformLoginSession Session, string Token) Register()
    {
        _users.Add(_user);
        var id = Guid.NewGuid();
        var token = _codec.Create(id);
        var session = PlatformLoginSession.Create(_user, id, token, _clock.GetUtcNow().UtcDateTime);
        _sessions.Add(session);
        return (session, token);
    }
    private Task<NOF.Contract.Result<LoginResponse>> Refresh(string token, Guid request)
        => new RefreshLogin(_sessions, _users, new Gate(), _codec, _issuer, _clock, _db)
            .HandleAsync(new() { RefreshToken = token, RequestId = request }, null!, default);

    [Fact]
    public async Task RefreshRotatesAndDuplicateRequestRecoversTheSameReplacement()
    {
        var (session, token) = Register();
        var request = Guid.NewGuid();
        var first = await Refresh(token, request);
        var retry = await Refresh(token, request);
        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.NotEqual(token, first.Value!.RefreshToken);
        Assert.Equal(first.Value.RefreshToken, retry.Value!.RefreshToken);
        Assert.Equal(session.Id, _issuer.SessionId);
        Assert.DoesNotContain(token, session.UsedTokensJson);
        Assert.NotEqual(token, session.TokenHash);
    }

    [Fact]
    public async Task ReusingAnOldTokenWithANewRequestRevokesTheWholeSession()
    {
        var (session, token) = Register();
        var first = await Refresh(token, Guid.NewGuid());
        Assert.False((await Refresh(token, Guid.NewGuid())).IsSuccess);
        Assert.NotNull(session.RevokedAtUtc);
        Assert.False((await Refresh(first.Value!.RefreshToken!, Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task RandomInvalidTokenCannotRevokeAnotherSession()
    {
        var (session, token) = Register();
        Assert.False((await Refresh(_codec.Create(session.Id), Guid.NewGuid())).IsSuccess);
        Assert.Null(session.RevokedAtUtc);
        Assert.True((await Refresh(token, Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task LogoutInvalidatesRefreshAndAccessSessionValidation()
    {
        var (session, token) = Register();
        var validator = new LoginSessionValidator(_sessions, _clock);
        Assert.True(await validator.IsActiveAsync(_user, session.Id.ToString("N"), default));
        await new Logout(_sessions, new Gate(), _clock, _db).HandleAsync(new() { RefreshToken = token }, null!, default);
        Assert.False(await validator.IsActiveAsync(_user, session.Id.ToString("N"), default));
        Assert.False((await Refresh(token, Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task PasswordResetInvalidatesBothRefreshAndAccess()
    {
        var (session, token) = Register();
        _user.ResetPassword("new-password-hash");
        Assert.False((await Refresh(token, Guid.NewGuid())).IsSuccess);
        Assert.False(await new LoginSessionValidator(_sessions, _clock).IsActiveAsync(_user, session.Id.ToString("N"), default));
    }

    [Fact]
    public async Task ExpiryAndDeletedUsersCannotRefresh()
    {
        var (_, token) = Register();
        _clock.Now = _clock.Now.AddDays(30);
        Assert.False((await Refresh(token, Guid.NewGuid())).IsSuccess);
        _clock.Now = _clock.Now.AddDays(-30);
        _users.Clear();
        Assert.False((await Refresh(token, Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task KnownOldGenerationRemainsAReplayAfterMultipleRotations()
    {
        var (session, token) = Register();
        var first = await Refresh(token, Guid.NewGuid());
        Assert.True((await Refresh(first.Value!.RefreshToken!, Guid.NewGuid())).IsSuccess);
        Assert.False((await Refresh(token, Guid.NewGuid())).IsSuccess);
        Assert.NotNull(session.RevokedAtUtc);
    }

    [Fact]
    public async Task MalformedTokensAreRejectedWithoutMutation()
    {
        Register();
        Assert.False((await Refresh("malformed", Guid.NewGuid())).IsSuccess);
        Assert.False((await Refresh("", Guid.Empty)).IsSuccess);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Issuer : IPlatformTokenIssuer
    {
        public Guid? SessionId { get; private set; }
        public IssuedAccessToken Issue(PlatformUser user, Guid? sessionId = null)
        { SessionId = sessionId; return new("fixture-access", DateTime.UtcNow.AddHours(1)); }
    }
    private sealed class Gate : IRegistrationLock
    {
        public Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
