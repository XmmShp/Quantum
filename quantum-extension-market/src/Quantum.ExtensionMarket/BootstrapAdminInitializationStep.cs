using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Domain;
using NOF.Hosting;
using Quantum.ExtensionMarket.Application;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket;

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
        var users = scope.ServiceProvider.GetRequiredService<IRepository<MarketUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IMarketPasswordHasher>();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var normalizedEmail = MarketUser.NormalizeEmail(configured.Email);
        if (await users.AsNoTracking().AnyAsync(user => user.Email == normalizedEmail))
        {
            return;
        }

        var user = MarketUser.Create(
            configured.Username,
            normalizedEmail,
            passwordHasher.Hash(configured.Password),
            MarketUserRole.User | MarketUserRole.Developer | MarketUserRole.Reviewer | MarketUserRole.Admin,
            idGenerator,
            timeProvider);
        await users.AddAsync(user);
        await dbContext.SaveChangesAsync();
        logger.LogInformation("Created the configured Extension Market bootstrap administrator {Username}.", user.Username);
    }
}
