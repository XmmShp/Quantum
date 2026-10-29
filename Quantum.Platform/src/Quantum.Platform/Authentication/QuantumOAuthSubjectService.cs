using NOF.Hosting.AspNetCore.Extension.OidcServer;

namespace Quantum.Platform.Authentication;

internal sealed class QuantumOAuthSubjectService : IOAuthSubjectService
{
    public ValueTask<OAuthSubjectProfile?> GetProfileAsync(
        string subject,
        IReadOnlySet<string> scopes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<OAuthSubjectProfile?>(OAuthSubjectProfile.Create(subject));
    }
}
