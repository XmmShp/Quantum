using NOF.Domain;
using Quantum.Platform.Application.Handlers;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Tests;

public sealed class MarketplaceVersionTests
{
    [Fact]
    public async Task MarketplaceLatestReleaseUsesVersionInsteadOfUploadTime()
    {
        var ids = new SnowflakeIdGenerator();
        var users = new PasswordResetTests.MemoryRepository<PlatformUser>();
        var listings = new PasswordResetTests.MemoryRepository<PluginListing>();
        var releases = new PasswordResetTests.MemoryRepository<PluginRelease>();
        var user = PlatformUser.Create("owner", "owner@example.com", "hash", PlatformUserRole.Admin, ids);
        users.Add(user);
        var listing = PluginListing.Create("quantum.plugin.calendar", "Calendar", "Calendar", user.Id, [], ids);
        listings.Add(listing);
        // Upload an older version after the newest, and leave a future version unpublished.
        foreach (var version in new[] { "1.10.0", "1.9.0", "1.10.0-beta.1", "2.0.0" })
        {
            var release = PluginRelease.Create(listing.Id, version, ">=0.1.0", "", $"{version}.zip", 1, new string('a', 64), ids);
            if (version != "2.0.0")
                release.Review(PluginReleaseStatus.Published, user.Id, null);
            releases.Add(release);
        }

        var list = await new ListPlugins(listings, releases, users).HandleAsync(new(), null!, default);
        Assert.True(list.IsSuccess);
        Assert.Equal("1.10.0", Assert.Single(list.Value).LatestRelease!.Version);

        var details = await new GetPlugin(listings, releases, users)
            .HandleAsync(new() { PluginId = listing.PluginId }, null!, default);
        Assert.True(details.IsSuccess);
        Assert.Equal("1.10.0", details.Value.Plugin.LatestRelease!.Version);
        Assert.Equal(new[] { "1.10.0", "1.10.0-beta.1", "1.9.0" }, details.Value.Releases.Select(release => release.Version));
    }
}
