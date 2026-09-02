using System.Globalization;

namespace Quantum.Localization;

public sealed class CultureService
{
    public const string PreferenceKey = "Quantum.Culture";
    public const string EnglishCultureName = "en-US";
    public const string ChineseCultureName = "zh-CN";

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo(EnglishCultureName);
    private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo(ChineseCultureName);
    private readonly Action<string>? _savePreference;

    public CultureService(string? requestedCultureName = null, Action<string>? savePreference = null)
    {
        _savePreference = savePreference;
        CurrentCulture = ResolveCulture(requestedCultureName ?? CultureInfo.CurrentUICulture.Name);
        ApplyCulture(CurrentCulture);
    }

    public event EventHandler? Changed;

    public CultureInfo CurrentCulture { get; private set; }

    public IReadOnlyList<CultureInfo> SupportedCultures { get; } =
        [ChineseCulture, EnglishCulture];

    public string TextDirection => CurrentCulture.TextInfo.IsRightToLeft ? "rtl" : "ltr";

    public void SetCulture(string? cultureName)
    {
        var culture = ResolveCulture(cultureName);
        if (string.Equals(CurrentCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentCulture = culture;
        ApplyCulture(culture);
        _savePreference?.Invoke(culture.Name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static CultureInfo ResolveCulture(string? cultureName)
    {
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                var requested = CultureInfo.GetCultureInfo(cultureName.Trim());
                if (string.Equals(
                        requested.TwoLetterISOLanguageName,
                        ChineseCulture.TwoLetterISOLanguageName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ChineseCulture;
                }
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return EnglishCulture;
    }

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
