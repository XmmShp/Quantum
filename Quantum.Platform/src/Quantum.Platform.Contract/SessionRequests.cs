namespace Quantum.Platform.Contract;

public sealed record RefreshLoginRequest
{
    public required string RefreshToken { get; set; }
    public required Guid RequestId { get; set; }
    public override string ToString() => "[REDACTED]";
}

public sealed record LogoutRequest
{
    public required string RefreshToken { get; set; }
    public override string ToString() => "[REDACTED]";
}
