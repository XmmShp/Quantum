using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.Tests;

public sealed class ServiceProviderExtensionsTests
{
    [Fact]
    public void GetService_ResolvesRegisteredServiceByExactFullName()
    {
        using var services = new ServiceCollection()
            .AddSingleton<StringResolvedService>()
            .BuildServiceProvider();

        dynamic? service = services.GetService(typeof(StringResolvedService).FullName!);

        Assert.IsType<StringResolvedService>(service);
    }

    [Fact]
    public void GetService_ReturnsNullWhenTypeOrRegistrationDoesNotExist()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Null(services.GetService("Quantum.Tests.TypeThatDoesNotExist"));
        Assert.Null(services.GetService(typeof(StringResolvedService).FullName!));
    }

    [Fact]
    public void GetService_UsesRegistrationToDisambiguateTypesWithSameFullName()
    {
        var serviceTypeName = $"Quantum.Tests.RuntimeContracts.Service{Guid.NewGuid():N}";
        _ = CreateRuntimeType(serviceTypeName);
        var registeredType = CreateRuntimeType(serviceTypeName);
        using var services = new ServiceCollection()
            .AddSingleton(registeredType, registeredType)
            .BuildServiceProvider();

        var service = services.GetService(serviceTypeName);

        Assert.NotNull(service);
        Assert.Equal(registeredType, service.GetType());
    }

    [Fact]
    public void GetService_UsesProvidedScopeAndDoesNotCacheScopedInstance()
    {
        using var services = new ServiceCollection()
            .AddScoped<TrackedScopedService>()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });
        var serviceTypeName = typeof(TrackedScopedService).FullName!;

        Assert.Throws<InvalidOperationException>(() => services.GetService(serviceTypeName));

        TrackedScopedService first;
        using (var firstScope = services.CreateScope())
        {
            firstScope.ServiceProvider.ResolveDaemonServices();
            first = Assert.IsType<TrackedScopedService>(
                firstScope.ServiceProvider.GetService(serviceTypeName));
            var second = firstScope.ServiceProvider.GetService(serviceTypeName);

            Assert.Same(first, second);
            Assert.False(first.IsDisposed);
        }

        Assert.True(first.IsDisposed);

        using var nextScope = services.CreateScope();
        nextScope.ServiceProvider.ResolveDaemonServices();
        var fromNextScope = nextScope.ServiceProvider.GetService(serviceTypeName);
        Assert.NotSame(first, fromNextScope);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("global::Quantum.Tests.StringResolvedService")]
    [InlineData("Quantum.Tests.StringResolvedService, Quantum.Tests")]
    [InlineData(" Quantum.Tests.StringResolvedService")]
    public void GetService_RejectsNamesThatAreNotExactTypeFullNames(string serviceTypeName)
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentException>(() => services.GetService(serviceTypeName));
    }

    private static Type CreateRuntimeType(string fullName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Quantum.Tests.RuntimeContracts.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.RunAndCollect);
        var type = assembly
            .DefineDynamicModule("Main")
            .DefineType(fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType()!;
    }
}

public sealed class StringResolvedService;

public sealed class TrackedScopedService : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
