using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

public static class PluginReleaseDownloadPolicy
{
    public static bool CanDownload(
        PluginListing listing,
        PluginRelease release,
        PlatformUser? caller)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(release);

        if (release.Status == PluginReleaseStatus.Published)
        {
            return true;
        }

        return release.Status == PluginReleaseStatus.Pending &&
            caller is not null &&
            (listing.AuthorUserId == caller.Id ||
             caller.HasAnyRole(PlatformUserRole.Reviewer | PlatformUserRole.Admin));
    }
}
