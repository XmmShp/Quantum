using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Quantum.Plugins.Persistence;

internal static class PluginDatabaseMigrator
{
    private const string CreateHistoryTableSql = """
        CREATE TABLE IF NOT EXISTS "__quantum_plugin_migrations" (
            "plugin_id" TEXT NOT NULL,
            "migration_name" TEXT NOT NULL,
            "sha256" TEXT NOT NULL,
            "plugin_version" TEXT NOT NULL,
            "applied_at_utc" TEXT NOT NULL,
            CONSTRAINT "PK___quantum_plugin_migrations"
                PRIMARY KEY ("plugin_id", "migration_name")
        );
        """;

    public static async Task ApplyAsync(
        PluginManifest manifest,
        string pluginRootPath,
        string databasePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (manifest.Database is null)
        {
            return;
        }

        var migrationFiles = PluginMigrationArtifact.Discover(pluginRootPath, manifest.Database);
        var scripts = new List<PluginMigrationScript>(migrationFiles.Count);
        foreach (var migration in migrationFiles)
        {
            scripts.Add(await PluginMigrationArtifact.ReadAsync(migration, cancellationToken).ConfigureAwait(false));
        }

        var fullDatabasePath = Path.GetFullPath(databasePath);
        var databaseDirectory = Path.GetDirectoryName(fullDatabasePath)
            ?? throw new ArgumentException("Database path must have a parent directory.", nameof(databasePath));
        Directory.CreateDirectory(databaseDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullDatabasePath,
            Pooling = false,
            DefaultTimeout = 30
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = CreateHistoryTableSql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var applied = await ReadAppliedAsync(
                    connection,
                    transaction,
                    manifest.Id.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateHistory(manifest.Id.Value, scripts, applied);

            foreach (var script in scripts.Skip(applied.Count))
            {
                await using (var migrationCommand = connection.CreateCommand())
                {
                    migrationCommand.Transaction = transaction;
                    migrationCommand.CommandText = script.Sql;
                    await migrationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO "__quantum_plugin_migrations" (
                        "plugin_id", "migration_name", "sha256", "plugin_version", "applied_at_utc")
                    VALUES ($pluginId, $migrationName, $sha256, $pluginVersion, $appliedAtUtc);
                    """;
                historyCommand.Parameters.AddWithValue("$pluginId", manifest.Id.Value);
                historyCommand.Parameters.AddWithValue("$migrationName", script.Name);
                historyCommand.Parameters.AddWithValue("$sha256", script.Sha256);
                historyCommand.Parameters.AddWithValue("$pluginVersion", manifest.Version.ToString());
                historyCommand.Parameters.AddWithValue(
                    "$appliedAtUtc",
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await historyCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // A malformed script may have ended the Host-owned transaction itself.
            }

            throw;
        }
    }

    private static async Task<IReadOnlyList<AppliedMigration>> ReadAppliedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string pluginId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT "migration_name", "sha256"
            FROM "__quantum_plugin_migrations"
            WHERE "plugin_id" = $pluginId
            ORDER BY "rowid";
            """;
        command.Parameters.AddWithValue("$pluginId", pluginId);

        var result = new List<AppliedMigration>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AppliedMigration(reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    private static void ValidateHistory(
        string pluginId,
        IReadOnlyList<PluginMigrationScript> scripts,
        IReadOnlyList<AppliedMigration> applied)
    {
        if (applied.Count > scripts.Count)
        {
            throw new InvalidDataException(
                $"Plugin '{pluginId}' migration artifact is missing previously applied migrations.");
        }

        for (var index = 0; index < applied.Count; index++)
        {
            var expected = scripts[index];
            var actual = applied[index];
            if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Plugin '{pluginId}' migration history is not append-only: expected '{actual.Name}' at position {index + 1}, but the artifact contains '{expected.Name}'.");
            }

            if (!string.Equals(expected.Sha256, actual.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Plugin '{pluginId}' migration '{expected.Name}' was modified after it was applied.");
            }
        }
    }

    private sealed record AppliedMigration(string Name, string Sha256);
}
