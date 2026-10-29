using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;

namespace Quantum.Plugins;

internal static class PluginRpcServiceCollectionExtensions
{
    internal static IServiceCollection AddQuantumPluginRpc(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(PluginRpcContextInboundMiddleware)))
        {
            return services;
        }

        services.AddLogging();
        services.AddNOFApplication();
        services.TryAddSingleton<IHostEnvironment>(Host.CreateEmptyApplicationBuilder(null).Environment);
        services.TryAddScoped<RpcServerInvocationResolver>();
        services.TryAddScoped<RequestInboundPipelineExecutor>();
        services.TryAddScoped<IRequestOutboundPipelineExecutor, RequestOutboundPipelineExecutor>();
        services.TryAddScoped<PluginRpcContextInboundMiddleware>();
        services.AddScoped<IRequestInboundMiddleware>(provider => provider.GetRequiredService<PluginRpcContextInboundMiddleware>());
        services.AddRequestInboundMiddleware<TracingInboundMiddleware>();
        return services;
    }
}
