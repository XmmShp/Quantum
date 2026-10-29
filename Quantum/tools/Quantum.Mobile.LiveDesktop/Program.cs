using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quantum.Marketplace;
using Quantum.Mobile;
using Quantum.Plugins;
using Quantum.Plugin.Abstraction;

// Operator-run live probe: authenticates normally and loads the real desktop sample
// through the production runtime manager/ALC. It never simulates the platform or RPC handler.
string Required(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {name}");
}
var platform = new Uri(Required("--platform"));
var credentials = JsonSerializer.Deserialize<Credentials>(await File.ReadAllTextAsync(Required("--credentials")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var statePath = Path.GetFullPath(Required("--state"));
Directory.CreateDirectory(statePath);
var modules = Path.GetFullPath(Required("--modules"));
var services = new ServiceCollection().AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information))
    .AddQuantumPluginEventBus();
var catalog = new PluginCatalog([]);
services.AddSingleton(catalog);
services.AddSingleton<IQuantumPluginEnvironment>(catalog);
await using var provider = services.BuildServiceProvider();
await using var manager = new PluginRuntimeManager(catalog, new(modules, Path.Combine(statePath, "shadow"), Path.Combine(statePath, "desktop.db")),
    logger: provider.GetRequiredService<ILogger<PluginRuntimeManager>>());
await manager.InitializeAsync(provider);
if (catalog.Failures.Count != 0 || !catalog.IsPluginLoaded(PluginId.Of("quantum.plugin.example")))
    throw new InvalidOperationException("The real sample desktop plugin failed to load: " + string.Join("; ", catalog.Failures.Select(f => f.Reason)));
using var router = new PluginRpcRouter(catalog, provider.GetRequiredService<ILogger<PluginRpcRouter>>());
var gateway = new MobileActionGateway(catalog, router);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
var options = new QuantumMarketplaceOptions(platform, "0.1.0");
var session = new QuantumMarketplaceSession(() => Task.FromResult<string?>(null), _ => Task.CompletedTask, () => Task.CompletedTask);
var client = new QuantumMarketplaceClient(http, options, session);
await session.LoginAsync(client, credentials.Email, credentials.Password);
string? registration = null;
await using var desktop = new MobileDesktopBridge(http, session, options, gateway, Path.Combine(statePath, "journal"),
    () => Task.FromResult(registration), text => { registration = text; return Task.CompletedTask; }, () => registration = null);
await desktop.StartAsync(["quantum.plugin.example"]);
Console.WriteLine("LIVE_DESKTOP_READY " + platform.Host);
Console.WriteLine("Commands: bindings, approve <mobile-device-id>, status, stop");
while (await Console.In.ReadLineAsync() is { } command)
{
    if (command == "stop") break;
    if (command == "bindings")
        Console.WriteLine(JsonSerializer.Serialize(desktop.Bindings));
    else if (command.StartsWith("approve ", StringComparison.Ordinal))
    {
        var id = command[8..].Trim();
        if (!desktop.Bindings.Any(b => b.MobileId == id)) throw new ArgumentException("No matching pending device request.");
        await desktop.ApproveAsync(id, true);
        Console.WriteLine("LIVE_DEVICE_APPROVED " + id);
    }
    else if (command == "status") Console.WriteLine(desktop.Status);
}
await desktop.StopAsync();
Console.WriteLine("LIVE_DESKTOP_STOPPED");

internal sealed record Credentials(string Email, string Password);
