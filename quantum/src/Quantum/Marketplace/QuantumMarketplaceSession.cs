namespace Quantum.Marketplace;

public sealed class QuantumMarketplaceSession(
    Func<Task<string?>> readToken,
    Func<string, Task> writeToken,
    Func<Task> clearToken)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string? AccessToken { get; private set; }
    public MarketplaceUser? User { get; private set; }
    public bool IsAuthenticated => AccessToken is not null && User is not null;

    public async Task InitializeAsync(
        IQuantumMarketplaceClient client,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (AccessToken is not null)
            {
                return;
            }

            AccessToken = await readToken();
            if (AccessToken is null)
            {
                return;
            }

            try
            {
                User = await client.GetCurrentUserAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                AccessToken = null;
                User = null;
                await clearToken();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LoginAsync(
        IQuantumMarketplaceClient client,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var login = await client.LoginAsync(email, password, cancellationToken);
        AccessToken = login.AccessToken;
        User = login.User;
        await writeToken(login.AccessToken);
    }

    public async Task LogoutAsync()
    {
        AccessToken = null;
        User = null;
        await clearToken();
    }
}
