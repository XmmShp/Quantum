using System.Reflection;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Quantum.Marketplace;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class MarketplacePluginInstallerTests
{
    private const string Id = "quantum.plugin.calendar";
    private static readonly MarketplaceRelease Release = new("1.1.1", ">=0.1.0", "", 2, 86220, new string('a', 64), DateTime.UtcNow, 0);
    private static readonly MarketplacePlugin Plugin = new(Id, "Calendar", "Calendar", "Shoming", [], Release);

    [Fact]
    public async Task OneCallDownloadsPreflightsAndInstallsWithoutAnotherConfirmation()
    {
        var (installer, market, runtime) = Create();
        var result = await installer.InstallAsync(Plugin);
        Assert.True(result.Succeeded);
        Assert.Equal(1, market.Downloads);
        Assert.Equal(new[] { "PrepareInstallAsync", "InstallAsync", "CancelInstallAsync" }, runtime.Calls);
        Assert.Equal(new byte[] { 1, 2, 3 }, runtime.PreparedBytes);
    }

    [Fact]
    public async Task IncompatibilityDoesNotDownloadOrInstall()
    {
        var (installer, market, runtime) = Create();
        market.Compatibility = new(false, null, ">=99.0.0");
        Assert.False((await installer.InstallAsync(Plugin)).Succeeded);
        Assert.Equal(0, market.Downloads);
        Assert.Empty(runtime.Calls);
    }

    [Theory]
    [InlineData("quantum.plugin.other", "1.1.1")]
    [InlineData(Id, "9.0.0")]
    public async Task UnexpectedPackageIdentityCannotInstall(string id, string version)
    {
        var (installer, _, runtime) = Create();
        runtime.Preview = new("preview", "calendar.zip", [new(id, version, null, PluginInstallAction.Install)], []);
        Assert.False((await installer.InstallAsync(Plugin)).Succeeded);
        Assert.DoesNotContain("InstallAsync", runtime.Calls);
        Assert.Contains("CancelInstallAsync", runtime.Calls);
    }

    [Fact]
    public async Task PreflightFailureShowsReasonAndDoesNotInstall()
    {
        var (installer, _, runtime) = Create();
        runtime.Preview = runtime.Preview with { Issues = [new(Id, "Missing dependency")] };
        var result = await installer.InstallAsync(Plugin);
        Assert.False(result.Succeeded);
        Assert.Equal("Missing dependency", result.Message);
        Assert.DoesNotContain("InstallAsync", runtime.Calls);
    }

    [Fact]
    public async Task CancelledInstallStillReleasesItsStagedPreview()
    {
        var (installer, _, runtime) = Create();
        runtime.CancelDuringInstall = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(Plugin));
        Assert.Equal("CancelInstallAsync", runtime.Calls.Last());
    }

    [Theory]
    [InlineData(null, "1.1.1", "1.1.1", false, false)]
    [InlineData("1.0.0", "1.1.1", "1.1.1", true, false)]
    [InlineData("1.1.1", "1.1.1", "1.1.1", false, false)]
    [InlineData("2.0.0", "1.1.1", "1.1.1", false, false)]
    [InlineData("1.1.1+local", "1.1.1", "1.1.1", false, false)]
    [InlineData("1.9.0", "1.10.0", "1.10.0", true, false)]
    [InlineData("1.1.1-beta.2", "1.1.1-beta.10", "1.1.1-beta.10", true, false)]
    [InlineData("1.1.1-beta.1", "1.1.1", "1.1.1", true, false)]
    [InlineData("1.1.1", "2.0.0", "1.1.1", false, false)]
    [InlineData("1.1.1", "2.0.0", null, false, false)]
    [InlineData("1.0.0", "1.1.1", "1.1.1", true, true)]
    [InlineData("1.1.1", "1.1.1", "1.1.1", false, true)]
    public async Task MarketplaceOnlyOffersUpdatesForNewerCompatibleVersions(
        string? installed, string latest, string? compatible, bool expectUpdate, bool disabled)
    {
        var client = DispatchProxy.Create<IQuantumMarketplaceClient, MarketStub>();
        var market = (MarketStub)(object)client;
        market.ListedPlugin = Plugin with { LatestRelease = Release with { Version = latest } };
        market.Compatibility = new(compatible is not null, compatible, ">=0.1.0");
        var runtime = DispatchProxy.Create<IPluginRuntimeManager, RuntimeStub>();
        var catalog = new PluginCatalog([]);
        if (installed is not null)
        {
            if (disabled)
                ((RuntimeStub)(object)runtime).DisabledPlugins = [new(Id, installed)];
            else
                catalog.Replace([new LoadedPlugin(new PluginManifest(PluginId.Of(Id), SemanticVersion.Of(installed), "test.dll"),
                    ".", typeof(MarketplacePluginInstallerTests).Assembly, [])]);
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton(client);
        services.AddSingleton(runtime);
        services.AddSingleton(new QuantumMarketplaceOptions(new Uri("https://quantum.io-vii.com"), "0.1.0"));
        services.AddSingleton<MarketplacePluginInstaller>();
        services.AddSingleton(catalog);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<Quantum.Components.Pages.Marketplace>()).ToHtmlString());
        Assert.Contains($"v{latest}", html);
        Assert.DoesNotContain("v@", html);
        Assert.Contains("class=\"primary-button\"", html);
        Assert.Contains("Calendar", html);
        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.Equal(installed is null, text.Contains("Download and install", StringComparison.Ordinal) || text.Contains("下载并安装", StringComparison.Ordinal));
        Assert.Equal(expectUpdate, text.Contains("Update plugin", StringComparison.Ordinal) || text.Contains("更新插件", StringComparison.Ordinal));
        Assert.DoesNotContain("Check for update", text);
        Assert.DoesNotContain("检查更新", text);
        if (installed is not null)
            Assert.Contains($"v{installed}", text);
    }

    private static (MarketplacePluginInstaller, MarketStub, RuntimeStub) Create()
    {
        var client = DispatchProxy.Create<IQuantumMarketplaceClient, MarketStub>();
        var runtime = DispatchProxy.Create<IPluginRuntimeManager, RuntimeStub>();
        return (new(client, runtime, new(new Uri("https://quantum.io-vii.com"), "0.1.0")), (MarketStub)(object)client, (RuntimeStub)(object)runtime);
    }

    public class MarketStub : DispatchProxy
    {
        public MarketplacePlugin ListedPlugin = Plugin;
        public MarketplaceCompatibility Compatibility = new(true, "1.1.1", ">=0.1.0");
        public int Downloads;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_BaseAddress") return new Uri("https://quantum.io-vii.com");
            if (method!.Name == "CheckCompatibilityAsync") return Task.FromResult(Compatibility);
            if (method.Name == "DownloadAsync") { Downloads++; return Task.FromResult(new MarketplaceDownload("calendar.zip", "application/zip", [1, 2, 3], 3, "hash")); }
            if (method.Name == "ListPluginsAsync") return Task.FromResult<IReadOnlyList<MarketplacePlugin>>([ListedPlugin]);
            throw new NotSupportedException(method.Name);
        }
    }

    public class RuntimeStub : DispatchProxy
    {
        public IReadOnlyList<DisabledPluginInfo> DisabledPlugins = [];
        public List<string> Calls = [];
        public byte[]? PreparedBytes;
        public bool CancelDuringInstall;
        public PluginInstallPreview Preview = new("preview", "calendar.zip", [new(Id, "1.1.1", null, PluginInstallAction.Install)], []);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            if (method.Name == "GetDisabledPlugins") return DisabledPlugins;
            if (method.Name == "PrepareInstallAsync")
            {
                using var output = new MemoryStream();
                ((Stream)args![0]!).CopyTo(output);
                PreparedBytes = output.ToArray();
                return Task.FromResult(Preview);
            }
            if (method.Name == "InstallAsync") return CancelDuringInstall ? Task.FromException<PluginOperationResult>(new OperationCanceledException()) : Task.FromResult(PluginOperationResult.Success("Installed Calendar"));
            if (method.Name == "CancelInstallAsync") return Task.CompletedTask;
            throw new NotSupportedException(method.Name);
        }
    }
}
