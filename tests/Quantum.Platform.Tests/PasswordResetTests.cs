using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Application;
using NOF.Domain;
using Quantum.Platform.Application;
using Quantum.Platform.Application.Handlers;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Tests;

public sealed class PasswordResetTests
{
    private readonly SnowflakeIdGenerator ids = new();
    private readonly Clock clock = new();
    private readonly Pbkdf2PlatformPasswordHasher hasher = new();
    private readonly MemoryRepository<PlatformUser> users = new();
    private readonly MemoryRepository<PasswordResetVerification> codes = new();
    private readonly MemoryRepository<AuditEntry> audits = new();
    private readonly Mail mail = new();
    private readonly Generator generator = new();
    private readonly IDbContext db = DispatchProxy.Create<IDbContext, SaveOnlyContext>();
    private readonly Lock gate = new();

    private PlatformUser AddUser()
    {
        var user = PlatformUser.Create("developer", "developer@example.com", hasher.Hash("old-password-123"), idGenerator: ids);
        users.Add(user);
        return user;
    }

    private Task<NOF.Contract.Result> RequestCode(string email = "developer@example.com")
        => new RequestPasswordResetEmailCode(users, codes, hasher, generator, gate, mail, ids, clock, db,
            NullLogger<RequestPasswordResetEmailCode>.Instance)
            .HandleAsync(new() { Email = email }, null!, default);

    private Task<NOF.Contract.Result> Reset(string code = "123456", string password = "new-password-123")
        => new ResetPassword(users, codes, hasher, gate, new AuditWriter(audits, ids, clock), clock, db)
            .HandleAsync(new() { Email = "developer@example.com", VerificationCode = code, NewPassword = password }, null!, default);

    [Fact]
    public async Task Reset_ChangesPasswordAndConsumesCodeOnce()
    {
        var user = AddUser();
        Assert.True((await RequestCode(" Developer@Example.com ")).IsSuccess);
        Assert.True(hasher.Verify(codes.Single().CodeHash, "123456"));
        Assert.DoesNotContain("123456", codes.Single().CodeHash);
        Assert.True((await Reset()).IsSuccess);
        Assert.True(hasher.Verify(user.PasswordHash, "new-password-123"));
        Assert.False(hasher.Verify(user.PasswordHash, "old-password-123"));
        Assert.Empty(codes);
        Assert.Single(audits);
        Assert.False((await Reset()).IsSuccess);
    }

    [Fact]
    public async Task Requests_HideUnknownEmailsAndThrottleResends()
    {
        Assert.True((await RequestCode()).IsSuccess);
        Assert.Empty(mail.Messages);
        AddUser();
        await RequestCode();
        var originalHash = codes.Single().CodeHash;
        Assert.True((await RequestCode()).IsSuccess);
        Assert.Single(mail.Messages);
        Assert.Equal(originalHash, codes.Single().CodeHash);
        clock.Advance(TimeSpan.FromMinutes(1));
        await RequestCode();
        Assert.Equal(2, mail.Messages.Count);
        Assert.NotEqual(originalHash, codes.Single().CodeHash);
    }

    [Theory]
    [InlineData("999999")]
    [InlineData("bad-code")]
    public async Task WrongCodes_LockOutAfterFiveAttempts(string guess)
    {
        var user = AddUser();
        var originalHash = user.PasswordHash;
        await RequestCode();
        for (var i = 0; i < 5; i++) Assert.False((await Reset(guess)).IsSuccess);
        Assert.Equal(5, codes.Single().FailedAttempts);
        Assert.False((await Reset()).IsSuccess);
        Assert.Equal(originalHash, user.PasswordHash);
        clock.Advance(TimeSpan.FromMinutes(1));
        await RequestCode();
        Assert.Equal(0, codes.Single().FailedAttempts);
        Assert.True((await Reset()).IsSuccess);
    }

    [Fact]
    public async Task ExpiredCode_AndWeakPasswordCannotReset()
    {
        AddUser();
        await RequestCode();
        Assert.False((await Reset(password: "short")).IsSuccess);
        Assert.Equal(0, codes.Single().FailedAttempts);
        clock.Advance(PasswordResetVerification.Lifetime);
        Assert.False((await Reset()).IsSuccess);
        Assert.Empty(audits);
    }

    [Fact]
    public async Task Resend_InvalidatesThePreviousCode()
    {
        AddUser();
        await RequestCode();
        clock.Advance(TimeSpan.FromMinutes(1));
        generator.Code = "654321";
        await RequestCode();
        Assert.False((await Reset("123456")).IsSuccess);
        Assert.True((await Reset("654321")).IsSuccess);
    }

    [Fact]
    public async Task DeliveryFailure_DoesNotCreateCode()
    {
        AddUser();
        mail.Fail = true;
        Assert.True((await RequestCode()).IsSuccess);
        Assert.Empty(codes);
        Assert.False((await Reset()).IsSuccess);
    }

    [Fact]
    public async Task DeliveryFailure_PreservesPreviousCode()
    {
        AddUser();
        await RequestCode();
        var originalHash = codes.Single().CodeHash;
        clock.Advance(TimeSpan.FromMinutes(1));
        mail.Fail = true;
        Assert.True((await RequestCode()).IsSuccess);
        Assert.Equal(originalHash, codes.Single().CodeHash);
        Assert.True((await Reset()).IsSuccess);
    }

    [Fact]
    public async Task CodeCannotResetAnotherAccountReusingEmail()
    {
        var original = AddUser();
        await RequestCode();
        users.Remove(original);
        var replacement = AddUser();
        Assert.False((await Reset()).IsSuccess);
        Assert.True(hasher.Verify(replacement.PasswordHash, "old-password-123"));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
    private sealed class Generator : IEmailVerificationCodeGenerator
    {
        public string Code { get; set; } = "123456";
        public string Generate() => Code;
    }
    private sealed class Mail : IPlatformEmailSender
    {
        public List<string> Messages { get; } = [];
        public bool Fail { get; set; }
        public Task SendAsync(string recipient, string subject, string textBody, string htmlBody, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Test delivery failure");
            Messages.Add(textBody);
            return Task.CompletedTask;
        }
    }
    private sealed class Lock : IRegistrationLock
    {
        public Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken cancellationToken)
            => Task.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    public class SaveOnlyContext : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == "SaveChangesAsync" ? Task.FromResult(1) : throw new NotSupportedException();
    }
    internal sealed class MemoryRepository<T> : AsyncQueryable<T>, IRepository<T> where T : class
    {
        private readonly List<T> items;
        public MemoryRepository() : this([]) { }
        private MemoryRepository(List<T> items) : base(items.AsQueryable(), InMemoryAsyncQueryExecutor.Instance)
            => this.items = items;
        public int Count => items.Count;
        public bool IsReadOnly => false;
        public void Add(T entity) => items.Add(entity);
        public ValueTask AddAsync(T entity, CancellationToken cancellationToken = default) { Add(entity); return ValueTask.CompletedTask; }
        public void AddRange(IEnumerable<T> entities) => items.AddRange(entities);
        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { AddRange(entities); return Task.CompletedTask; }
        public void Attach(T entity) { }
        public void AttachRange(IEnumerable<T> entities) { }
        public void Update(T entity) { }
        public void UpdateRange(IEnumerable<T> entities) { }
        public bool Remove(T entity) => items.Remove(entity);
        public void RemoveRange(IEnumerable<T> entities) { foreach (var entity in entities.ToArray()) Remove(entity); }
        public IAsyncQueryable<T> AsNoTracking() => this;
        public void Clear() => items.Clear();
        public bool Contains(T entity) => items.Contains(entity);
        public void CopyTo(T[] array, int index) => items.CopyTo(array, index);
    }
}
