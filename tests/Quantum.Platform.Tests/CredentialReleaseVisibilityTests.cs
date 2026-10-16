using System.Globalization;
using NOF.Domain;
using Quantum.Platform.Application;
using Quantum.Platform.Application.Handlers;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Tests;

public sealed class CredentialReleaseVisibilityTests
{
    [Theory]
    [InlineData(true, true, 2)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 1)]
    public async Task MachineIdentitySeesPendingOnlyWithinBothPermissions(bool publish, bool plugin, int count)
    {
        var ids = new SnowflakeIdGenerator();
        var users = new PasswordResetTests.MemoryRepository<PlatformUser>();
        var listings = new PasswordResetTests.MemoryRepository<PluginListing>();
        var releases = new PasswordResetTests.MemoryRepository<PluginRelease>();
        var user = PlatformUser.Create("owner", "owner@example.com", "hash", PlatformUserRole.Admin, ids);
        users.Add(user);
        var listing = PluginListing.Create("quantum.plugin.calendar", "Calendar", "Calendar", user.Id, [], ids);
        listings.Add(listing);
        var pending = PluginRelease.Create(listing.Id, "1.1.1", ">=0.1.0", "", "pending.zip", 1, new string('a', 64), ids);
        var published = PluginRelease.Create(listing.Id, "1.1.0", ">=0.1.0", "", "published.zip", 1, new string('b', 64), ids);
        published.Review(PluginReleaseStatus.Published, user.Id, null);
        releases.AddRange([pending, published]);
        var caller = new Caller(user.Id.ToString()!, publish, plugin);
        var result = await new ListPluginReleases(listings, releases, new PlatformCallerResolver(caller, users))
            .HandleAsync(new() { PluginId = listing.PluginId }, null!, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(count, result.Value.Length);
    }

    private sealed class Caller(string userId, bool publish, bool plugin) : IPlatformCallerContext
    {
        public string? UserId => userId;
        public string? CredentialClientId => "test-ci";
        public bool HasPermission(string permission)
            => permission == QuantumPlatformPermissions.PluginPublish ? publish : plugin && permission == "plugin:quantum.plugin.calendar";
    }
}
