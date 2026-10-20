using NOF.Contract;
using System.Text.Json;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins;

internal static class OptionalRpc
{
    // Resolve all required methods from one provider, then pin qualified names.
    public static async Task<Dictionary<string, string>> DiscoverAsync(IRpcInvoker? invoker, params string[] required)
    {
        if (invoker is null)
        {
            return [];
        }

        try
        {
            var catalog = await invoker.InvokeAsync<JsonElement>("quantum.rpc.catalog", new { }, Context.Empty);
            if (!catalog.IsSuccess || catalog.Value.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            foreach (var service in catalog.Value.GetProperty("services").EnumerateArray().OrderBy(service => service.GetProperty("pluginId").GetString(), StringComparer.Ordinal))
            {
                var methods = service.GetProperty("methods").EnumerateArray().ToDictionary(
                    method => method.GetProperty("canonicalName").GetString()!,
                    method => method.GetProperty("qualifiedName").GetString()!);
                if (required.All(methods.ContainsKey))
                {
                    return methods;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Optional discovery must never prevent the owning page from loading.
        }

        return [];
    }

    public static async Task<T> CallAsync<T>(IRpcInvoker invoker, Dictionary<string, string> methods, string name, object request)
        => RpcResults.ValueOrThrow(await invoker.InvokeAsync<T>(methods[name], request, Context.Empty));
}
