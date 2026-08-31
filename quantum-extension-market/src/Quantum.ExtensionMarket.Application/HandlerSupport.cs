using System.Globalization;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket.Application;

internal static class HandlerSupport
{
    public static bool TryParseUserId(string? value, out MarketUserId id)
        => TryParse(value, MarketUserId.Of, out id);

    public static bool TryParseListingId(string? value, out PluginListingId id)
        => TryParse(value, PluginListingId.Of, out id);

    public static bool TryParseReleaseId(string? value, out PluginReleaseId id)
        => TryParse(value, PluginReleaseId.Of, out id);

    public static bool TryParseAuditEntryId(string? value, out AuditEntryId id)
        => TryParse(value, AuditEntryId.Of, out id);

    public static bool CanManage(PluginListing listing, MarketUser caller)
        => listing.AuthorUserId == caller.Id || caller.HasAnyRole(MarketUserRole.Admin);

    public static bool CanReview(MarketUser caller)
        => caller.HasAnyRole(MarketUserRole.Reviewer | MarketUserRole.Admin);

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
