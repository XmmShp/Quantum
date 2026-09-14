using System.Globalization;
using System.Resources;

namespace Quantum.OfficialPlugins.Calendar.Localization;

internal static class PluginText
{
    private static readonly ResourceManager Resources = new(
        "Quantum.OfficialPlugins.Calendar.Localization.PluginStrings",
        typeof(PluginStrings).Assembly);

    public static string Get(string name, params object?[] arguments)
    {
        var value = Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
        return arguments.Length == 0
            ? value
            : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }
}
