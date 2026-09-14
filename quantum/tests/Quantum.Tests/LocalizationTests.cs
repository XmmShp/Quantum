using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
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
    public void ApplicationResources_ProvideEnglishTranslation()
    {
        var resources = new ResourceManager(
            "Quantum.Localization.AppStrings",
            typeof(AppStrings).Assembly);

        var value = resources.GetString("设置", CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Settings", value);
    }

    [Fact]
    public void SetCulture_PersistsBeforePublishingOneConsistentChange()
    {
        var observations = new List<string>();
        var service = new CultureService(
            CultureService.EnglishCultureName,
            cultureName => observations.Add($"save:{cultureName}"));
        service.Changed += (_, _) => observations.Add(
            $"change:{service.CurrentCulture.Name}:{CultureInfo.CurrentUICulture.Name}");

        var changed = service.SetCulture(CultureService.ChineseCultureName);

        Assert.True(changed);
        Assert.Equal(
            ["save:zh-CN", "change:zh-CN:zh-CN"],
            observations);
    }

    [Fact]
    public void SetCulture_IgnoresRepeatedSelection()
    {
        var saveCount = 0;
        var changeCount = 0;
        var service = new CultureService(
            CultureService.ChineseCultureName,
            _ => saveCount++);
        service.Changed += (_, _) => changeCount++;

        var changed = service.SetCulture("zh-Hans");

        Assert.False(changed);
        Assert.Equal(0, saveCount);
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void SetCulture_RapidChangesFinishAtLastPersistedSelection()
    {
        var persisted = string.Empty;
        var service = new CultureService(
            CultureService.EnglishCultureName,
            cultureName => persisted = cultureName);

        service.SetCulture(CultureService.ChineseCultureName);
        service.SetCulture(CultureService.EnglishCultureName);
        service.SetCulture(CultureService.ChineseCultureName);

        Assert.Equal(CultureService.ChineseCultureName, service.CurrentCulture.Name);
        Assert.Equal(CultureService.ChineseCultureName, CultureInfo.CurrentUICulture.Name);
        Assert.Equal(CultureService.ChineseCultureName, persisted);
    }

    [Fact]
    public void CultureAwareLocalizer_UsesExplicitSelectionInsteadOfAmbientRenderCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var service = new CultureService(CultureService.EnglishCultureName);
            IStringLocalizer<AppStrings> localizer = new CultureAwareStringLocalizer<AppStrings>(service);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(CultureService.ChineseCultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CultureService.ChineseCultureName);

            Assert.Equal("Settings", localizer["设置"].Value);

            service.SetCulture(CultureService.ChineseCultureName);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(CultureService.EnglishCultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CultureService.EnglishCultureName);

            Assert.Equal("设置", localizer["设置"].Value);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = originalCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalUiCulture;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
