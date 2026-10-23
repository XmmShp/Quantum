using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Xunit;
using CalendarPage = Quantum.OfficialPlugins.Calendar.UI.Pages.Calendar;
using CalendarStrings = Quantum.OfficialPlugins.Calendar.UI.Localization.PluginStrings;

namespace Quantum.LedgerPlugin.Tests;

public sealed class UILocalizationTests
{
    [Theory]
    [InlineData(typeof(OfficialPlugins.Calendar.CalendarPlugin), "Title is required.", "标题不能为空。")]
    [InlineData(typeof(OfficialPlugins.Ledger.LedgerPlugin), "Category is required.", "分类不能为空。")]
    [InlineData(typeof(OfficialPlugins.Codex.CodexPlugin), "Codex is not connected.", "Codex 尚未连接。")]
    public void MovedUIResourcesResolveInBothCultures(Type plugin, string message, string chinese)
    {
        var marker = plugin.Assembly.GetType(plugin.Namespace + ".UI.Localization.PluginStrings", throwOnError: true)!;
        var localizer = CreateFactory().Create(marker);
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.Equal(chinese, localizer[message].Value);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(message, localizer[message].Value);
            Assert.False(localizer[message].ResourceNotFound);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task CalendarLocalizesEnglishFailuresAtTheUIBoundary()
    {
        var page = new CalendarPage(null!);
        typeof(CalendarPage).GetProperty("L", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new StringLocalizer<CalendarStrings>(CreateFactory()));
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Func<Task> action = () => Task.FromException(new InvalidOperationException("Title is required."));
            await (Task)typeof(CalendarPage).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [action])!;
            Assert.Equal("标题不能为空。", typeof(CalendarPage).GetField("_errorMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData(typeof(OfficialPlugins.Calendar.CalendarPlugin), "calendar")]
    [InlineData(typeof(OfficialPlugins.Ledger.LedgerPlugin), "ledger")]
    [InlineData(typeof(OfficialPlugins.Codex.CodexPlugin), "codex")]
    public void ManifestComponentsResolveAfterTheUIMove(Type plugin, string name)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "plugin-manifests", name + ".json")));
        var ui = manifest.RootElement.GetProperty("ui");
        foreach (var item in ui.GetProperty("routes").EnumerateArray())
        {
            AssertComponent(plugin.Assembly, item);
        }
        if (ui.TryGetProperty("globalComponents", out var globalComponents))
        {
            foreach (var item in globalComponents.EnumerateArray())
            {
                AssertComponent(plugin.Assembly, item);
            }
        }
    }

    private static void AssertComponent(Assembly assembly, JsonElement item)
    {
        var name = item.GetProperty("component").GetString()!;
        Assert.Contains(".UI.Pages.", name);
        var component = assembly.GetType(name, throwOnError: true)!;
        Assert.True(typeof(IComponent).IsAssignableFrom(component));
    }

    private static ResourceManagerStringLocalizerFactory CreateFactory()
        => new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
}
