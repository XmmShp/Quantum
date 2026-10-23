namespace Quantum.Platform.Domain;

/// <summary>MVP bounded mailbox aggregate. The application holds a PostgreSQL account lock while updating it.</summary>
public sealed class MobileBridgeAccount
{
    public PlatformUserId Id { get; private set; }
    public string StateJson { get; private set; } = "{}";
    public long Version { get; private set; }
    private MobileBridgeAccount() { }
    public static MobileBridgeAccount Create(PlatformUserId userId) => new() { Id = userId };
    public void Update(string stateJson)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(stateJson) > 10 * 1024 * 1024)
            throw new ArgumentException("Mobile mailbox quota exceeded.");
        StateJson = stateJson;
        Version++;
    }
}
