using Npgsql;
using Quantum.Platform.Application;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Quantum.Platform;

public sealed class PostgreSqlRegistrationLock(IConfiguration configuration) : IRegistrationLock
{
    private readonly string connectionString = configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:postgres is required.");

    public async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var lockId = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@lock_id)", connection);
            command.Parameters.AddWithValue("lock_id", lockId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new Lease(connection, lockId);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection, long lockId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lock_id)", connection);
                command.Parameters.AddWithValue("lock_id", lockId);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
