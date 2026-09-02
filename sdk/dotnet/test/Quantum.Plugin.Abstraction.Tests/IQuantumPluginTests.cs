using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Quantum.Plugin.Abstraction.Tests;

public sealed class IQuantumPluginTests
{
    [Fact]
    public void StaticServiceConfigurationSupportsDefaultAndPluginImplementations()
    {
        var defaultServices = new ServiceCollection();
        ConfigureServices<DefaultConfigureServicesPlugin>(defaultServices);
        Assert.Empty(defaultServices);

        var configuredServices = new ServiceCollection();
        ConfigureServices<TestPlugin>(configuredServices);
        Assert.Contains(
            configuredServices,
            static descriptor => descriptor.ServiceType == typeof(ConfiguredService));
    }

    [Fact]
    public async Task StaticLifecycleCanBeInvokedWithoutCreatingBootstrapInstance()
    {
        var services = new TestServiceProvider();

        await StartAsync<TestPlugin>(services);
        await StopAsync<TestPlugin>(services);

        Assert.Equal(2, services.InvocationCount);
    }

    private static Task StartAsync<TPlugin>(IServiceProvider services)
        where TPlugin : IQuantumPlugin
        => TPlugin.StartAsync(services);

    private static void ConfigureServices<TPlugin>(IServiceCollection services)
        where TPlugin : IQuantumPlugin
        => TPlugin.ConfigureServices(services);

    private static Task StopAsync<TPlugin>(IServiceProvider services)
        where TPlugin : IQuantumPlugin
        => TPlugin.StopAsync(services);

    private sealed class TestPlugin : IQuantumPlugin
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ConfiguredService>();
        }

        public static Task StartAsync(
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            ((TestServiceProvider)services).InvocationCount++;
            return Task.CompletedTask;
        }

        public static Task StopAsync(
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            ((TestServiceProvider)services).InvocationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class DefaultConfigureServicesPlugin : IQuantumPlugin
    {
        public static Task StartAsync(
            IServiceProvider services,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public static Task StopAsync(
            IServiceProvider services,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ConfiguredService;

    private sealed class TestServiceProvider : IServiceProvider
    {
        public int InvocationCount { get; set; }

        public object? GetService(Type serviceType) => null;
    }
}
