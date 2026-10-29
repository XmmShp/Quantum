namespace Quantum.Mobile.Internal;

public static class PlatformAddress
{
    public const string DefaultUrl = "https://quantum.io-vii.com/";

    public static Uri Parse(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath.TrimEnd('/').EndsWith("/portal", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.TrimEnd('/').EndsWith("/rpc", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeException("invalid_platform_address", "Use an HTTPS platform base URL. HTTP is allowed only for localhost.");
        }
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
