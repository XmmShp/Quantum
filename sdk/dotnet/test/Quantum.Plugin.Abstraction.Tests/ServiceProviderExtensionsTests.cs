using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Quantum.Plugin.Abstraction.Tests;

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

        Assert.Null(services.GetService("Quantum.Plugin.Abstraction.Tests.TypeThatDoesNotExist"));
        Assert.Null(services.GetService(typeof(StringResolvedService).FullName!));
    }

    [Fact]
    public void GetService_UsesRegistrationToDisambiguateTypesWithSameFullName()
    {
        var serviceTypeName = $"Quantum.Plugin.Abstraction.Tests.RuntimeContracts.Service{Guid.NewGuid():N}";
        var duplicateType = CreateRuntimeType(serviceTypeName);
        var registeredType = CreateRuntimeType(serviceTypeName);
        using var services = new ServiceCollection()
            .AddSingleton(registeredType, registeredType)
            .BuildServiceProvider();

        var service = services.GetService(serviceTypeName);

        Assert.NotNull(service);
        Assert.Equal(registeredType, service.GetType());
        GC.KeepAlive(duplicateType);
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
    [InlineData("global::Quantum.Plugin.Abstraction.Tests.StringResolvedService")]
    [InlineData("Quantum.Plugin.Abstraction.Tests.StringResolvedService, Quantum.Plugin.Abstraction.Tests")]
    [InlineData(" Quantum.Plugin.Abstraction.Tests.StringResolvedService")]
    public void GetService_RejectsNamesThatAreNotExactTypeFullNames(string serviceTypeName)
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentException>(() => services.GetService(serviceTypeName));
    }

    [Fact]
    public void GetService_DoesNotKeepCollectibleServiceTypeAlive()
    {
        var assemblyReference = ResolveCollectibleService();

        for (var attempt = 0; attempt < 10 && assemblyReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(assemblyReference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ResolveCollectibleService()
    {
        var serviceTypeName = $"Quantum.Plugin.Abstraction.Tests.RuntimeContracts.Service{Guid.NewGuid():N}";
        var serviceType = CreateRuntimeType(serviceTypeName);
        var service = Activator.CreateInstance(serviceType)!;
        var services = new SingleServiceProvider(serviceType, service);

        Assert.Same(service, services.GetService(serviceTypeName));

        return new WeakReference(serviceType.Assembly, trackResurrection: false);
    }

    private static Type CreateRuntimeType(string fullName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Quantum.Plugin.Abstraction.Tests.RuntimeContracts.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.RunAndCollect);
        var type = assembly
            .DefineDynamicModule("Main")
            .DefineType(fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType()!;
    }

    private sealed class SingleServiceProvider(Type serviceType, object service) : IServiceProvider
    {
        public object? GetService(Type requestedType)
            => requestedType == serviceType ? service : null;
    }
}

public sealed class StringResolvedService;

public sealed class TrackedScopedService : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
