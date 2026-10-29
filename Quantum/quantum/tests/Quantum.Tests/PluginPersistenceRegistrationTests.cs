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
    public void AddPersistence_PreservesPluginMappingsAndAmbientProjection()
    {
        var services = new ServiceCollection();
        services.AddMapping<OrderingRequest, OrderingRequest>(request => new OrderingRequest(request.Value));
        var registry = services.GetOrAddSingleton<MappingRegistry>();

        services.AddQuantumPluginPersistence(Path.Combine(Path.GetTempPath(), "quantum-mapping-registration.db"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        Assert.Same(registry, provider.GetRequiredService<MappingRegistry>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(MappingRegistry));
        var result = new[] { new OrderingRequest("mapped") }.AsQueryable().ProjectTo<OrderingRequest>().Single();
        Assert.Equal("mapped", result.Value);
    }

    [Fact]
    public void AddPersistence_PreservesPluginRpcServerRegistry()
    {
        var services = new ServiceCollection();
        services.AddRpcServer<OrderingRpcServer>();
        var registry = services.GetOrAddSingleton<RpcServerRegistry>();

        services.AddQuantumPluginPersistence(Path.Combine(Path.GetTempPath(), "quantum-registration.db"));

        using var provider = services.BuildServiceProvider();
        Assert.Same(registry, provider.GetRequiredService<RpcServerRegistry>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RpcServerRegistry));
        Assert.True(registry.TryGetImplementationType(typeof(IOrderingRpcService), out var server));
        Assert.Equal(typeof(OrderingRpcServer), server);
    }

    [Fact]
    public async Task AddPersistence_KeepsLocalRpcCallsWorking()
    {
        var services = new ServiceCollection();
        services.AddNOFApplication();
        services.AddRpcServer<OrderingRpcServer>();
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
