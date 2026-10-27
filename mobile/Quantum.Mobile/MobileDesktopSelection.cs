using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

internal static class MobileDesktopSelection
{
    public static BridgeDevice? Choose(IEnumerable<BridgeDevice> devices, string? rememberedId, DateTimeOffset now, string? rememberedName = null)
    {
        var desktops = devices.Where(d => d.Kind == "desktop").ToArray();
        if (!string.IsNullOrEmpty(rememberedId))
        {
            // An offline remembered computer must not silently redirect the user to another one.
            var previous = desktops.FirstOrDefault(d => d.Id == rememberedId);
            if (previous is not null) return previous;
            // Registration IDs can rotate after desktop restart or credential expiry.
            // Recover a uniquely named online desktop in the same account; never guess between duplicates.
            var replacements = desktops.Where(d => d.Name == rememberedName).Take(2).ToArray();
            return replacements.Length == 1 && IsOnline(replacements[0], now) ? replacements[0] : null;
        }
        return desktops.Where(d => IsOnline(d, now))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    public static bool IsOnline(BridgeDevice device, DateTimeOffset now) => device.SeenAt > now.AddSeconds(-45);
}
