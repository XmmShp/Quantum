using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Quantum.Marketplace;

public sealed class QuantumMarketplaceClient : IQuantumMarketplaceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public QuantumMarketplaceClient(HttpClient httpClient, QuantumMarketplaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        BaseAddress = options.BaseAddress;
    }

    public Uri BaseAddress { get; }

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
            cancellationToken);

    public Task<MarketplacePluginDetails> GetPluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
        => InvokeAsync<MarketplacePluginDetails>(
            "GetPlugin",
            new { pluginId = RequirePluginId(pluginId) },
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

    private async Task<T> InvokeAsync<T>(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method,
            @params = parameters
        };
        using var response = await _httpClient.PostAsJsonAsync(
            new Uri(BaseAddress, "rpc"),
            request,
            JsonOptions,
            cancellationToken);
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
}
