using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quantum.MobileApp.Debugging;

/// <summary>Small, opt-in HTTP endpoint. This file is excluded from non-Debug app builds.</summary>
public sealed class MobileDebugServer : IAsyncDisposable
{
    private const int MaximumHeaderBytes = 8192;
    private const int MaximumBodyBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 64 };
    private readonly TcpListener _listener;
    private readonly byte[] _authorization;
    private readonly IMobileDebugTarget _target;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private TcpClient? _activeClient;
    public int Port { get; }

    public MobileDebugServer(MobileDebugOptions options, IMobileDebugTarget target)
    {
        options.Validate();
        _target = target;
        _authorization = Encoding.ASCII.GetBytes("Bearer " + options.Token);
        _listener = new(IPAddress.Loopback, options.Port);
        _listener.Start(8);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _worker = RunAsync();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _activeClient = client;
                client.NoDelay = true;
                await HandleAsync(client.GetStream());
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            finally { _activeClient = null; }
        }
    }

    private async Task HandleAsync(NetworkStream stream)
    {
        var status = 200;
        object result;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var bytes = new List<byte>();
            var single = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(single, deadline.Token) == 0) return;
                bytes.Add(single[0]);
                if (bytes.Count > MaximumHeaderBytes) throw new MobileDebugException(431, "debug_headers_too_large");
                if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10) break;
            }
            var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n", StringSplitOptions.None);
            var request = lines[0].Split(' ');
            if (request.Length != 3 || request[2] is not ("HTTP/1.1" or "HTTP/1.0")) throw BadRequest();
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0 || !headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim())) throw BadRequest();
            }
            if (!headers.TryGetValue("Host", out var host) ||
                host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}") throw new MobileDebugException(403, "debug_host_rejected");
            if (headers.ContainsKey("Origin") || headers.TryGetValue("Sec-Fetch-Site", out var site) && site != "none")
                throw new MobileDebugException(403, "debug_browser_rejected");
            var provided = Encoding.ASCII.GetBytes(headers.GetValueOrDefault("Authorization", ""));
            if (!CryptographicOperations.FixedTimeEquals(provided, _authorization)) throw new MobileDebugException(401, "debug_unauthorized");
            if (headers.ContainsKey("Transfer-Encoding")) throw BadRequest();
            var length = 0;
            if (headers.TryGetValue("Content-Length", out var rawLength) && (!int.TryParse(rawLength, out length) || length < 0)) throw BadRequest();
            if (length > MaximumBodyBytes) throw new MobileDebugException(413, "debug_body_too_large");
            var path = request[1];
            var get = path is "/state" or "/devices" or "/pages";
            var post = path is "/desktop/select" or "/page/open" or "/page/refresh" or "/input" or "/press" or "/preview" or "/preview/close" or "/navigation/open" or "/navigation/close" or "/navigation/select";
            if (!get && !post) throw new MobileDebugException(404, "debug_route_not_found");
            if (get && request[0] != "GET" || post && request[0] != "POST") throw new MobileDebugException(405, "debug_method_not_allowed");
            if (get && length != 0) throw BadRequest();
            if (post && (!headers.TryGetValue("Content-Type", out var contentType) ||
                !contentType.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase)))
                throw new MobileDebugException(415, "debug_json_required");
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, deadline.Token);
            using var document = JsonDocument.Parse(length == 0 ? "{}"u8.ToArray() : body, new() { MaxDepth = 12 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw BadRequest();
            // Do not retry or cancel a business action when an HTTP client disconnects.
            // The normal mobile pending/outcome journal remains authoritative.
            var value = await _target.ExecuteAsync(path, document.RootElement.Clone()).WaitAsync(_stop.Token);
            result = new { ok = true, value };
        }
        catch (MobileDebugException e) { status = e.Status; result = new { ok = false, error = e.Code }; }
        catch (JsonException) { status = 400; result = new { ok = false, error = "debug_invalid_json" }; }
        catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { status = 408; result = new { ok = false, error = "debug_request_timeout" }; }
        catch (Exception) when (!_stop.IsCancellationRequested) { status = 500; result = new { ok = false, error = "debug_operation_failed" }; }
        byte[] json;
        try { json = JsonSerializer.SerializeToUtf8Bytes(result, Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            status = 500;
            json = "{\"ok\":false,\"error\":\"debug_response_unavailable\"}"u8.ToArray();
        }
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Debug\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {json.Length}\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n");
        using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        writeDeadline.CancelAfter(TimeSpan.FromSeconds(5));
        await stream.WriteAsync(header, writeDeadline.Token);
        await stream.WriteAsync(json, writeDeadline.Token);
    }

    private static MobileDebugException BadRequest() => new(400, "debug_bad_request");

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        _activeClient?.Dispose();
        await _worker;
        _stop.Dispose();
        CryptographicOperations.ZeroMemory(_authorization);
    }
}
