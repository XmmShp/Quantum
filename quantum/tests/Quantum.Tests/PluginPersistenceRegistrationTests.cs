using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;
using Quantum.Plugins.Persistence;

namespace Quantum.Tests;

public sealed class PluginPersistenceRegistrationTests
{
    [Fact]
    public void AddPersistence_PreservesPluginRpcServerRegistry()
    {
        var services = new ServiceCollection();
        var registry = new RpcServerRegistry();
        services.AddSingleton(registry);

        services.AddQuantumPluginPersistence(Path.Combine(Path.GetTempPath(), "quantum-registration.db"));

        using var provider = services.BuildServiceProvider();
        Assert.Same(registry, provider.GetRequiredService<RpcServerRegistry>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RpcServerRegistry));
    }

    [Fact]
    public async Task AddPersistence_KeepsLocalRpcCallsWorking()
    {
        var services = new ServiceCollection();
        services.AddNOFApplication();
        var registry = services.GetOrAddSingleton<RpcServerRegistry>();
        registry.Add(new RpcServerRegistration(typeof(IOrderingRpcService), typeof(OrderingRpcServer)));
        services.AddScoped<OrderingRpcServer>();
        services.AddScoped<RpcServerInvocationResolver>();
        services.AddScoped<RequestInboundPipelineExecutor>();
        services.AddScoped<IRequestOutboundPipelineExecutor, RequestOutboundPipelineExecutor>();
        services.AddSingleton(new RpcProviderMarker("quantum.plugin.ledger"));
        services.AddTransient<OrderingRpcServer.Ping, OrderingPing>();
        services.AddTransient<OrderingRpcServer.Reset, OrderingReset>();
        services.AddQuantumPluginPersistence(Path.Combine(Path.GetTempPath(), "quantum-registration.db"));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var result = await RpcServerInvoker.InvokeAsync<IOrderingRpcService>(
            scope.ServiceProvider,
            typeof(IOrderingRpcService).GetMethod(nameof(IOrderingRpcService.Ping))!,
            new OrderingRequest(),
            Context.Empty,
            CancellationToken.None);

        var response = Assert.IsType<Result<string>>(result);
        Assert.True(response.IsSuccess, response.Message);
        Assert.Equal("quantum.plugin.ledger", response.Value);
    }
}
