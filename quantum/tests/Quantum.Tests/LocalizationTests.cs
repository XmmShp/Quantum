using System.Globalization;
using System.Resources;
using Quantum.Localization;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant-TW")]
    public void ResolveCulture_NormalizesChineseCultures(string requestedCulture)
    {
        var culture = CultureService.ResolveCulture(requestedCulture);

        Assert.Equal(CultureService.ChineseCultureName, culture.Name);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("")]
    [InlineData("not_a_culture")]
    public void ResolveCulture_FallsBackToEnglish(string requestedCulture)
    {
        var culture = CultureService.ResolveCulture(requestedCulture);

        Assert.Equal(CultureService.EnglishCultureName, culture.Name);
    }

    [Fact]
    public void PluginLoadContext_LoadsCultureSpecificSatelliteAssembly()
    {
        var entryPath = typeof(Quantum.ExampleCalendarPlugin.CalendarPlugin).Assembly.Location;
        var loadContext = new PluginLoadContext(entryPath);
        try
        {
            var pluginAssembly = loadContext.LoadEntryAssembly();

            var satellite = pluginAssembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("en-US", satellite.GetName().CultureName);
            Assert.EndsWith(".resources", satellite.GetName().Name, StringComparison.Ordinal);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ApplicationResources_ProvideEnglishTranslation()
    {
        var resources = new ResourceManager(
            "Quantum.Localization.AppStrings",
            typeof(AppStrings).Assembly);

        var value = resources.GetString("设置", CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Settings", value);
    }
}
