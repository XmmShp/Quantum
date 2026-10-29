using System.Globalization;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

internal static class HandlerSupport
{
    public static bool TryParseUserId(string? value, out PlatformUserId id)
        => TryParse(value, PlatformUserId.Of, out id);

    public static bool TryParseListingId(string? value, out PluginListingId id)
        => TryParse(value, PluginListingId.Of, out id);

    public static bool TryParseReleaseId(string? value, out PluginReleaseId id)
        => TryParse(value, PluginReleaseId.Of, out id);

    public static bool TryParseAuditEntryId(string? value, out AuditEntryId id)
        => TryParse(value, AuditEntryId.Of, out id);

    public static bool CanManage(PluginListing listing, PlatformUser caller)
        => listing.AuthorUserId == caller.Id || caller.HasAnyRole(PlatformUserRole.Admin);

    public static bool CanReview(PlatformUser caller)
        => caller.HasAnyRole(PlatformUserRole.Reviewer | PlatformUserRole.Admin);

    private static bool TryParse<T>(string? value, Func<long, T> factory, out T id)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            id = factory(parsed);
            return true;
        }

        id = default!;
        return false;
    }
}
