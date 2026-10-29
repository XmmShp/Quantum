using Quantum.Mobile.Internal;

namespace Quantum.Marketplace;

public sealed class QuantumMarketplaceSession(
    Func<Task<string?>> readToken,
    Func<string, Task> writeToken,
    Func<Task> clearToken)
{
    private PlatformSession? _authentication;
    private Uri? _platform;
    internal PlatformSession Authentication => _authentication ?? throw new InvalidOperationException("Create the marketplace client before using its session.");
    public event Action? Changed;
    public string? AccessToken => _authentication?.AccessToken;
    public MarketplaceUser? User => _authentication?.User is { } user
        ? new(user.UserId, user.Username, user.Email, (MarketplaceUserRoles)user.Roles) : null;
    public bool IsAuthenticated => _authentication?.IsAuthenticated == true;

    internal void Configure(HttpClient http, Uri platform)
    {
        if (_authentication is not null)
        {
            if (_platform != platform) throw new InvalidOperationException("A login session cannot be shared across platforms.");
            return;
        }
        _platform = platform;
        _authentication = new(http, platform, readToken, writeToken, clearToken);
        _authentication.Changed += () => Changed?.Invoke();
    }

    public async Task InitializeAsync(IQuantumMarketplaceClient client, CancellationToken cancellationToken = default)
        => await Authentication.RestoreAsync(cancellationToken);

    public Task LoginAsync(IQuantumMarketplaceClient client, string email, string password, CancellationToken cancellationToken = default)
        => Authentication.LoginAsync(email, password, cancellationToken);

    public Task LogoutAsync() => Authentication.LogoutAsync();
}
