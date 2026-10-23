using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;
using System.Text.Json;
using Xunit;

namespace Quantum.Tests;

public sealed class CodexAutoConnectionTests
{
    private static CodexRuntime Service(GatedInvoker invoker, TimeSpan? timeout = null) =>
        new(invoker, NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, timeout);

    [Fact]
    public async Task PageAndDrawerShareOneAttemptFailurePersistsAndManualRetryWorks()
    {
        var invoker = new GatedInvoker();
        await using var service = Service(invoker);
        var attempts = Enumerable.Range(0, 20).Select(_ => service.EnsureAutoConnectedAsync()).ToArray();
        Assert.All(attempts, task => Assert.Same(attempts[0], task));
        await invoker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CodexConnectionState.Connecting, service.Snapshot.ConnectionState);
        invoker.Release.TrySetResult();
        await Task.WhenAll(attempts);
        Assert.Equal(1, invoker.Calls);
        Assert.Equal(CodexConnectionState.Failed, service.Snapshot.ConnectionState);
        Assert.Contains("Catalog unavailable <detail>", service.Snapshot.ErrorMessage);
        await service.EnsureAutoConnectedAsync();
        Assert.Equal(1, invoker.Calls);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexRuntime.DefaultModel);
        Assert.Equal(2, invoker.Calls);
    }

    [Fact]
    public async Task ManualDisconnectBeforeFirstRenderSuppressesAutomaticConnection()
    {
        var invoker = new GatedInvoker();
        await using var service = Service(invoker);
        await service.DisconnectAsync();
        await service.EnsureAutoConnectedAsync();
        Assert.Equal(0, invoker.Calls);
        Assert.Equal(CodexConnectionState.Disconnected, service.Snapshot.ConnectionState);
    }

    [Fact]
    public async Task DisconnectCancelsAnInFlightAutomaticAttemptAndDoesNotReconnectOnNavigation()
    {
        var invoker = new GatedInvoker();
        await using var service = Service(invoker);
        var automatic = service.EnsureAutoConnectedAsync();
        await invoker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await automatic;
        await service.EnsureAutoConnectedAsync();
        Assert.Equal(1, invoker.Calls);
        Assert.Equal(CodexConnectionState.Disconnected, service.Snapshot.ConnectionState);
        Assert.Null(service.Snapshot.ErrorMessage);
    }

    [Fact]
    public async Task AutomaticTimeoutBecomesAVisibleFailure()
    {
        var invoker = new GatedInvoker();
        await using var service = Service(invoker, TimeSpan.FromMilliseconds(100));
        await service.EnsureAutoConnectedAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CodexConnectionState.Failed, service.Snapshot.ConnectionState);
        Assert.Contains("timed out", service.Snapshot.ErrorMessage);
    }

    [Fact]
    public async Task PluginDisposalCancelsAutomaticConnectionWithoutWaitingForTimeout()
    {
        var invoker = new GatedInvoker();
        var service = Service(invoker);
        var automatic = service.EnsureAutoConnectedAsync();
        await invoker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await automatic;
        Assert.Equal(CodexConnectionState.Disconnected, service.Snapshot.ConnectionState);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.EnsureAutoConnectedAsync());
    }

    [Fact]
    public async Task ConnectionFailureRendersTheActualReasonOnThePageWithARetryAction()
    {
        var invoker = new GatedInvoker();
        invoker.Release.TrySetResult();
        await using var service = Service(invoker);
        await service.EnsureAutoConnectedAsync();
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(null);
        NOF.Hosting.NOFInfrastructureExtensions.AddNOFInfrastructure(builder);
        NOF.Hosting.NOFHostingExtensions.AddApplicationPart(builder, typeof(OfficialPlugins.Codex.CodexPlugin).Assembly);
        var services = builder.Services;
        services.AddScoped<IRequestOutboundPipelineExecutor, NOF.Hosting.RequestOutboundPipelineExecutor>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddLocalization();
        OfficialPlugins.Codex.CodexPlugin.ConfigureServices(services);
        services.AddSingleton<ICodexRuntime>(service);
        services.AddSingleton<IComponentActivator, InjectingActivator>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OfficialPlugins.Codex.UI.Pages.Codex>();
            return rendered.ToHtmlString();
        });
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Catalog unavailable &lt;detail&gt;", html);
        Assert.Contains("Retry connection", html);
        Assert.DoesNotContain("Catalog unavailable <detail>", html);
    }

    private sealed class InjectingActivator : IComponentActivator
    {
        private readonly IServiceProvider _services;

        public InjectingActivator(IServiceProvider services)
        {
            _services = services;
        }

        public IComponent CreateInstance(Type type) => (IComponent)ActivatorUtilities.CreateInstance(_services, type);
    }

    private sealed class GatedInvoker : IRpcInvoker
    {
        public int Calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Result<T>> InvokeAsync<T>(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
        {
            Assert.Equal("quantum.rpc.catalog", rpcName);
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Result.Fail("catalog_unavailable", "Catalog unavailable <detail>");
        }

        public Task<Result> InvokeAsync(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
