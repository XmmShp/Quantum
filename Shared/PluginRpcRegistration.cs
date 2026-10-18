using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;

namespace Quantum.OfficialPlugins;

/// <summary>Local NOF dispatch in a plugin's IServiceCollection-only bootstrap.</summary>
internal static class PluginRpcRegistration
{
    public static void AddPluginRpcServer<TServer>(this IServiceCollection services)
        where TServer : RpcServer, IRpcServer
    {
        services.AddNOFApplication();
        services.TryAddScoped<TServer>();
        var registry = services.GetOrAddSingleton<RpcServerRegistry>();
        registry.RemoveWhere(item => item.ServiceType == TServer.ServiceType);
        registry.Add(new RpcServerRegistration(TServer.ServiceType, typeof(TServer)));
        services.TryAddScoped<RpcServerInvocationResolver>();
        services.TryAddScoped<RequestInboundPipelineExecutor>();
        services.TryAddScoped<IRequestOutboundPipelineExecutor, RequestOutboundPipelineExecutor>();
    }
}
