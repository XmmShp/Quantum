using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Domain;
using NOF.Hosting;
using Quantum.Platform.Application;
using Quantum.Platform.Domain;

namespace Quantum.Platform;

public sealed class BootstrapAdminInitializationStep(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapAdminOptions> options,
    ILogger<BootstrapAdminInitializationStep> logger) : IApplicationInitializationStep
{
    public TopologyComparison Compare(IApplicationInitializationStep other)
        => other.GetType().Name == "DbContextMigrationInitializationStep"
            ? TopologyComparison.After
            : TopologyComparison.DoesNotMatter;

    public async Task ExecuteAsync(IHost app)
    {
        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.Password))
        {
            return;
        }

        if (configured.Password.Length < 12 || string.IsNullOrWhiteSpace(configured.Username) ||
            string.IsNullOrWhiteSpace(configured.Email))
        {
            throw new InvalidOperationException(
                $"{BootstrapAdminOptions.SectionName} requires Username, Email and a Password of at least 12 characters.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<PlatformUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPlatformPasswordHasher>();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var normalizedEmail = PlatformUser.NormalizeEmail(configured.Email);
        if (await users.AsNoTracking().AnyAsync(user => user.Email == normalizedEmail))
        {
            return;
        }

        var user = PlatformUser.Create(
            configured.Username,
            normalizedEmail,
            passwordHasher.Hash(configured.Password),
            PlatformUserRole.User | PlatformUserRole.Developer | PlatformUserRole.Reviewer | PlatformUserRole.Admin,
            idGenerator,
            timeProvider);
        await users.AddAsync(user);
        await dbContext.SaveChangesAsync();
        logger.LogInformation("Created the configured Quantum Platform bootstrap administrator {Username}.", user.Username);
    }
}
