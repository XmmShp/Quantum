using Microsoft.EntityFrameworkCore;

namespace Quantum.Plugins.Persistence;

internal sealed class PluginDatabaseInitializer(QuantumPluginDbContext database)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var createScript = database.Database.GenerateCreateScript();
        if (string.IsNullOrWhiteSpace(createScript))
        {
            return;
        }

        var idempotentScript = createScript
            .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.Ordinal)
            .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.Ordinal)
            .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.Ordinal);
        await database.Database.ExecuteSqlRawAsync(idempotentScript, cancellationToken);
    }
}
