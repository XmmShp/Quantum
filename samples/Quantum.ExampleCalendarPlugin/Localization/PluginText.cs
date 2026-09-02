using System.Globalization;
using System.Resources;

namespace Quantum.ExampleCalendarPlugin.Localization;

internal static class PluginText
{
    private static readonly ResourceManager Resources = new(
        "Quantum.ExampleCalendarPlugin.Localization.PluginStrings",
        typeof(PluginStrings).Assembly);

    public static string Get(string key, params object?[] arguments)
    {
        var format = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return arguments.Length == 0
            ? format
            : string.Format(CultureInfo.CurrentCulture, format, arguments);
    }
}
