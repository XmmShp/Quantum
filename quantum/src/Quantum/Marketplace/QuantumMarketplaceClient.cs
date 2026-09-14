using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Quantum.Marketplace;

public sealed class QuantumMarketplaceClient : IQuantumMarketplaceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly QuantumMarketplaceSession? _session;

    public QuantumMarketplaceClient(
        HttpClient httpClient,
        QuantumMarketplaceOptions options,
        QuantumMarketplaceSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _session = session;
        BaseAddress = options.BaseAddress;
    }

    public Uri BaseAddress { get; }

    public Task<MarketplaceLogin> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
        => InvokeAsync<MarketplaceLogin>(
            "Login",
            new { email = email.Trim(), password },
            AuthenticationMode.None,
            cancellationToken);

    public Task<MarketplaceUser> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        => InvokeAsync<MarketplaceUser>("GetCurrentUser", new { }, AuthenticationMode.Required, cancellationToken);

    public async Task<IReadOnlyList<MarketplacePlugin>> ListPluginsAsync(
        string? search = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
        => await InvokeAsync<MarketplacePlugin[]>(
            "ListPlugins",
            new
            {
                search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                tags = tags?.Where(static tag => !string.IsNullOrWhiteSpace(tag)).ToArray() ?? []
            },
            AuthenticationMode.None,
            cancellationToken);

    public Task<MarketplacePluginDetails> GetPluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
        => InvokeAsync<MarketplacePluginDetails>(
            "GetPlugin",
            new { pluginId = RequirePluginId(pluginId) },
            AuthenticationMode.None,
            cancellationToken);

    public Task<MarketplaceCompatibility> CheckCompatibilityAsync(
        string pluginId,
        string quantumVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quantumVersion);
        return InvokeAsync<MarketplaceCompatibility>(
            "CheckCompatibility",
            new { pluginId = RequirePluginId(pluginId), quantumVersion = quantumVersion.Trim() },
            AuthenticationMode.None,
            cancellationToken);
    }

    public async Task<MarketplaceDownload> DownloadAsync(
        string pluginId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var response = await InvokeAsync<DownloadResponse>(
            "DownloadPluginRelease",
            new { pluginId = RequirePluginId(pluginId), version = version.Trim() },
            AuthenticationMode.Optional,
            cancellationToken);

        byte[] archive;
        try
        {
            archive = Convert.FromBase64String(response.PackageArchiveBase64);
        }
        catch (FormatException exception)
        {
            throw new MarketplaceException(
                $"The marketplace returned an invalid package for '{pluginId}@{version}'.",
                exception);
        }

        if (archive.LongLength != response.PackageSizeBytes)
        {
            throw new MarketplaceException($"The downloaded package size for '{pluginId}@{version}' does not match its metadata.");
        }

        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(archive));
        if (!string.Equals(actualSha256, response.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new MarketplaceException($"The downloaded package checksum for '{pluginId}@{version}' does not match its metadata.");
        }

        return new MarketplaceDownload(
            response.FileName,
            response.ContentType,
            archive,
            response.PackageSizeBytes,
            response.PackageSha256);
    }

    public async Task<IReadOnlyList<MarketplacePlugin>> ListManagedPluginsAsync(
        CancellationToken cancellationToken = default)
        => await InvokeAsync<MarketplacePlugin[]>(
            "ListManagedPlugins",
            new { },
            AuthenticationMode.Required,
            cancellationToken);

    public async Task<IReadOnlyList<MarketplaceRelease>> ListPluginReleasesAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
        => await InvokeAsync<MarketplaceRelease[]>(
            "ListPluginReleases",
            new { pluginId = RequirePluginId(pluginId) },
            AuthenticationMode.Required,
            cancellationToken);

    private async Task<T> InvokeAsync<T>(
        string method,
        object parameters,
        AuthenticationMode authenticationMode,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method,
            @params = parameters
        };
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, "rpc"))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        var accessToken = _session?.AccessToken;
        if (authenticationMode == AuthenticationMode.Required && string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MarketplaceException("Marketplace authentication is required.");
        }

        if (!string.IsNullOrWhiteSpace(accessToken) && authenticationMode != AuthenticationMode.None)
        {
            requestMessage.Headers.Authorization = new("Bearer", accessToken);
        }

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MarketplaceException(
                $"The marketplace returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (TryGetProperty(root, "error", out var rpcError) && rpcError.ValueKind != JsonValueKind.Null)
        {
            throw new MarketplaceException(GetErrorMessage(rpcError));
        }

        if (!TryGetProperty(root, "result", out var result))
        {
            throw new MarketplaceException("The marketplace response did not contain a JSON-RPC result.");
        }

        if (TryGetProperty(result, "isSuccess", out var isSuccess)
            && isSuccess.ValueKind is JsonValueKind.False)
        {
            throw new MarketplaceException(GetErrorMessage(result));
        }

        var value = TryGetProperty(result, "value", out var resultValue)
            ? resultValue
            : result;
        return value.Deserialize<T>(JsonOptions)
            ?? throw new MarketplaceException($"The marketplace returned an empty result for '{method}'.");
    }

    private static string GetErrorMessage(JsonElement element)
    {
        if (TryGetProperty(element, "message", out var message)
            && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString()!;
        }

        if (TryGetProperty(element, "errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errors.EnumerateArray())
            {
                if (TryGetProperty(error, "message", out message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString()!;
                }
            }
        }

        return "The marketplace rejected the request.";
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string RequirePluginId(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return pluginId.Trim();
    }

    private sealed record DownloadResponse(
        string FileName,
        string ContentType,
        string PackageArchiveBase64,
        long PackageSizeBytes,
        string PackageSha256);

    private enum AuthenticationMode
    {
        None,
        Optional,
        Required
    }
}
