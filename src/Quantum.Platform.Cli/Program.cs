using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

return await QuantumCli.RunAsync(args);

internal static class QuantumCli
{
    private const string DefaultBaseUrl = "https://quantum.io-vii.com";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            if (args[0] is "--version" or "version")
            {
                Console.WriteLine(typeof(QuantumCli).Assembly.GetName().Version?.ToString(3) ?? "unknown");
                return 0;
            }

            if (args is ["plugins", "publish", .. var rest])
            {
                return await PublishAsync(rest);
            }

            if (args is ["clients", "create", .. var clientArgs])
            {
                return await CreateClientAsync(clientArgs);
            }

            throw new CliException("Unknown command. Run 'quantum-cli --help'.");
        }
        catch (Exception exception) when (exception is CliException or HttpRequestException or IOException or JsonException or CryptographicException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> CreateClientAsync(string[] args)
    {
        var options = Parse(args);
        var clientId = Require(options, "client-id");
        var displayName = Require(options, "display-name");
        var plugins = Require(options, "plugins").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var outputPath = Path.GetFullPath(Require(options, "private-jwks-output"));
        var baseUrl = Get(options, "base-url") ?? Environment.GetEnvironmentVariable("QUANTUM_PLATFORM_URL") ?? DefaultBaseUrl;
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var userToken = Environment.GetEnvironmentVariable("QUANTUM_USER_TOKEN");
        if (string.IsNullOrWhiteSpace(userToken))
        {
            var email = Require(options, "email");
            var password = ReadPassword("Password: ");
            var login = await SendRpcAsync(http, "Login", new { email, password }, null);
            userToken = login.GetProperty("accessToken").GetString()
                ?? throw new CliException("The platform did not return a user access token.");
        }

        var created = await SendRpcAsync(
            http,
            "CreateCredentialClient",
            new { clientId, displayName, allowedPluginIds = plugins },
            userToken);
        var privateJwks = created.GetProperty("generatedKeySet").GetProperty("privateJsonWebKeySet").GetString()
            ?? throw new CliException("The platform did not return generated private JWKS.");
        await using (var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(privateJwks);
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(outputPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Console.WriteLine($"Created Credential Client '{clientId}'. Private JWKS was written once to {outputPath}.");
        return 0;
    }

    private static async Task<JsonElement> SendRpcAsync(HttpClient http, string method, object parameters, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rpc");
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString("N"), method, @params = parameters }, options: JsonOptions);
        using var response = await http.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new CliException($"{method} failed with HTTP {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("error", out var rpcError))
            throw new CliException(rpcError.GetProperty("message").GetString() ?? $"{method} failed.");
        var result = document.RootElement.GetProperty("result");
        if (result.TryGetProperty("isSuccess", out var success) && !success.GetBoolean())
            throw new CliException(result.TryGetProperty("message", out var message) ? message.GetString() ?? $"{method} failed." : $"{method} failed.");
        return result.TryGetProperty("value", out var value) ? value.Clone() : result.Clone();
    }

    private static string ReadPassword(string prompt)
    {
        if (Console.IsInputRedirected) throw new CliException("Interactive password input requires a terminal. Alternatively set QUANTUM_USER_TOKEN.");
        Console.Error.Write(prompt);
        var value = new StringBuilder();
        while (Console.ReadKey(intercept: true) is { } key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && value.Length > 0) value.Length--;
            else if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return value.Length > 0 ? value.ToString() : throw new CliException("Password cannot be empty.");
    }

    private static async Task<int> PublishAsync(string[] args)
    {
        var options = Parse(args);
        var source = Require(options, "source");
        var support = Require(options, "quantum-version-support");
        var output = Get(options, "output") ?? "text";
        if (output is not ("text" or "subprocess")) throw new CliException("--output must be 'text' or 'subprocess'.");
        var waitText = Get(options, "wait-seconds") ?? "0";
        if (!int.TryParse(waitText, out var waitSeconds) || waitSeconds is < 0 or > 3600)
            throw new CliException("--wait-seconds must be an integer between 0 and 3600.");
        var baseUrl = Get(options, "base-url") ?? Environment.GetEnvironmentVariable("QUANTUM_PLATFORM_URL") ?? DefaultBaseUrl;
        var clientId = Get(options, "client-id") ?? Environment.GetEnvironmentVariable("QUANTUM_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new CliException("Set QUANTUM_CLIENT_ID or pass --client-id.");
        }

        var privateJwks = LoadPrivateJwks(options);
        var archive = Directory.Exists(source) ? CreateArchive(source) : await File.ReadAllBytesAsync(source);
        var manifest = ReadManifest(archive);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(archive));
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var tokenEndpoint = new Uri(http.BaseAddress, "oauth2/token");
        var assertion = CreateAssertion(clientId, tokenEndpoint, privateJwks);
        using var tokenResponse = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = assertion,
            ["scope"] = "plugin:publish"
        }));
        var tokenPayload = await tokenResponse.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<TokenResponse>(tokenPayload, JsonOptions);
        if (!tokenResponse.IsSuccessStatusCode || string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new CliException(token?.ErrorDescription ?? token?.Error ?? $"Token request failed with HTTP {(int)tokenResponse.StatusCode}.");
        }

        var existing = await FindReleaseAsync(http, token.AccessToken, manifest);
        JsonElement release;
        if (existing is { } found)
        {
            if (found.GetProperty("packageSha256").GetString() != sha256 ||
                found.GetProperty("quantumVersionSupport").GetString() != support)
                throw new CliException("This version already exists with a different package or compatibility range. Bump plugin.json version.");
            release = found;
        }
        else
        {
            release = await SendRpcAsync(http, "UploadPluginRelease", new
            {
                pluginId = manifest.Id,
                version = manifest.Version,
                quantumVersionSupport = support,
                releaseNotes = Get(options, "release-notes") ?? string.Empty,
                packageArchiveBase64 = Convert.ToBase64String(archive),
                expectedSha256 = sha256
            }, token.AccessToken);
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(waitSeconds);
        while (true)
        {
            var state = ReleasePublication.Evaluate(release);
            if (state == "Published" || waitSeconds == 0) break;
            if (DateTimeOffset.UtcNow >= deadline)
                throw new CliException($"Timed out waiting for {manifest.Id}@{manifest.Version} to publish. The release remains submitted; rerun to check it.");
            Console.Error.WriteLine($"Waiting for automated review: {manifest.Id}@{manifest.Version} ({state}).");
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp((deadline - DateTimeOffset.UtcNow).TotalSeconds, 0, 10)));
            release = await FindReleaseAsync(http, token.AccessToken, manifest)
                ?? throw new CliException("The submitted release is no longer visible to this client.");
        }

        var status = ReleasePublication.Evaluate(release);
        if (output == "subprocess")
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, pluginId = manifest.Id, version = manifest.Version, sha256, status, releaseId = release.GetProperty("releaseId").GetString() }, JsonOptions));
        }
        else if (output == "text")
        {
            Console.WriteLine($"{status}: {manifest.Id}@{manifest.Version} ({sha256}).");
        }
        else
        {
            throw new CliException("--output must be 'text' or 'subprocess'.");
        }

        return 0;
    }

    private static async Task<JsonElement?> FindReleaseAsync(HttpClient http, string token, PluginManifest manifest)
    {
        var releases = await SendRpcAsync(http, "ListPluginReleases", new { pluginId = manifest.Id }, token);
        foreach (var release in releases.EnumerateArray())
            if (release.GetProperty("version").GetString() == manifest.Version) return release.Clone();
        return null;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (!values.TryAdd("source", args[index])) throw new CliException("Only one package path may be supplied.");
                continue;
            }

            var name = args[index][2..];
            if (++index >= args.Length) throw new CliException($"Missing value for --{name}.");
            values[name] = args[index];
        }

        return values;
    }

    private static string LoadPrivateJwks(IReadOnlyDictionary<string, string> options)
    {
        var path = Get(options, "client-private-jwks");
        if (!string.IsNullOrWhiteSpace(path)) return File.ReadAllText(path);
        var value = Environment.GetEnvironmentVariable("QUANTUM_CLIENT_PRIVATE_JWKS");
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CliException("Set QUANTUM_CLIENT_PRIVATE_JWKS or pass --client-private-jwks FILE. Never pass private key material as an argument.");
    }

    private static byte[] CreateArchive(string source)
    {
        var root = Path.GetFullPath(source);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => !IsExcluded(Path.GetRelativePath(root, file)))
                .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var input = File.OpenRead(file);
                using var destination = entry.Open();
                input.CopyTo(destination);
            }
        }
        return output.ToArray();
    }

    private static bool IsExcluded(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.Any(part => part is ".git" or "bin" or "obj");
    }

    private static PluginManifest ReadManifest(byte[] archive)
    {
        using var stream = new MemoryStream(archive, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.Entries.SingleOrDefault(item => item.FullName.Equals("plugin.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException("The package root must contain plugin.json.");
        using var manifestStream = entry.Open();
        var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestStream, JsonOptions);
        return !string.IsNullOrWhiteSpace(manifest?.Id) && !string.IsNullOrWhiteSpace(manifest.Version)
            ? manifest
            : throw new CliException("plugin.json must contain id and version.");
    }

    private static string CreateAssertion(string clientId, Uri audience, string privateJwks)
    {
        using var document = JsonDocument.Parse(privateJwks);
        var key = document.RootElement.GetProperty("keys")[0];
        var parameters = new RSAParameters
        {
            Modulus = Decode(key.GetProperty("n").GetString()!),
            Exponent = Decode(key.GetProperty("e").GetString()!),
            D = Decode(key.GetProperty("d").GetString()!),
            P = Decode(key.GetProperty("p").GetString()!),
            Q = Decode(key.GetProperty("q").GetString()!),
            DP = Decode(key.GetProperty("dp").GetString()!),
            DQ = Decode(key.GetProperty("dq").GetString()!),
            InverseQ = Decode(key.GetProperty("qi").GetString()!)
        };
        using var rsa = RSA.Create();
        rsa.ImportParameters(parameters);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = key.GetProperty("kid").GetString() }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new { iss = clientId, sub = clientId, aud = audience.AbsoluteUri, iat = now, exp = now + 120, jti = Guid.NewGuid().ToString("N") }));
        var input = $"{header}.{payload}";
        return $"{input}.{Encode(rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}";
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
    private static string Require(IReadOnlyDictionary<string, string> values, string key) => Get(values, key) ?? throw new CliException($"Missing required {(key == "source" ? "package path" : $"--{key}")}.");
    private static string? Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static void PrintHelp() => Console.WriteLine("""
        Quantum Platform CLI

        quantum-cli clients create --email <email> --client-id <id> --display-name <name> --plugins <id,id> --private-jwks-output <file>
        quantum-cli plugins publish <folder-or-zip> --quantum-version-support <range> [options]

        Options:
          --release-notes <text>
          --base-url <url>                 or QUANTUM_PLATFORM_URL
          --client-id <id>                 or QUANTUM_CLIENT_ID
          --client-private-jwks <file>      or QUANTUM_CLIENT_PRIVATE_JWKS
          --output text|subprocess
          --wait-seconds <0-3600>           wait for Published; default 0 only submits

        Client setup prompts for the password without echoing it; QUANTUM_USER_TOKEN can be used instead.
        """);

    private sealed record PluginManifest(string Id, string Version);
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
    internal sealed class CliException(string message) : Exception(message);
}
