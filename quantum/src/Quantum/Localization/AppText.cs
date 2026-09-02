using System.Globalization;
using System.Resources;

namespace Quantum.Localization;

internal static class AppText
{
    private static readonly ResourceManager Resources = new(
        "Quantum.Localization.AppStrings",
        typeof(AppStrings).Assembly);

    public static string Get(string key, params object?[] arguments)
    {
        var format = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return arguments.Length == 0
            ? format
            : string.Format(CultureInfo.CurrentCulture, format, arguments);
    }
}
