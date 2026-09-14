using Quantum.Platform.Application;
using Quantum.Platform.Domain;
using NOF.Domain;

namespace Quantum.Platform.Tests;

public sealed class DomainTests
{
    private static readonly IIdGenerator IdGenerator = new SnowflakeIdGenerator();

    [Fact]
    public void PluginListing_NormalizesIdentityAndTags()
    {
        var listing = PluginListing.Create(
            "Quantum.Plugin.Example",
            " Example ",
            " Description ",
            PlatformUserId.Of(42),
            ["Utility", "utility", " Desktop "],
            IdGenerator);

        Assert.Equal("quantum.plugin.example", listing.PluginId);
        Assert.Equal("Example", listing.Name);
        Assert.Equal(["desktop", "utility"], listing.Tags);
    }

    [Theory]
    [InlineData(">=2.0.0 <3.0.0", "2.4.1", true)]
    [InlineData("2.0.0-2.9.9", "3.0.0", false)]
    [InlineData("2.*", "2.1.0", true)]
    [InlineData("2.1.0", "2.1.1", false)]
    public void QuantumVersionConstraint_MatchesSupportedForms(
        string expression,
        string version,
        bool expected)
        => Assert.Equal(expected, QuantumVersionConstraint.Contains(expression, version));

    [Fact]
    public void PublishedRelease_CanRecordDownload()
    {
        var release = PluginRelease.Create(
            PluginListingId.Of(1),
            "1.0.0",
            ">=2.0.0 <3.0.0",
            "Initial release",
            "quantum.plugin.example/1.0.0.zip",
            128,
            new string('a', 64),
            IdGenerator);

        release.Review(PluginReleaseStatus.Published, PlatformUserId.Of(2), "Approved");
        release.RecordDownload();

        Assert.Equal(PluginReleaseStatus.Published, release.Status);
        Assert.Equal(1, release.DownloadCount);
    }

    [Fact]
    public void PendingRelease_IsDownloadableOnlyByOwnerOrReviewer()
    {
        var owner = PlatformUser.Create(
            "owner",
            "owner@example.com",
            "password-hash",
            PlatformUserRole.User | PlatformUserRole.Developer,
            IdGenerator);
        var stranger = PlatformUser.Create(
            "stranger",
            "stranger@example.com",
            "password-hash",
            PlatformUserRole.User | PlatformUserRole.Developer,
            IdGenerator);
        var reviewer = PlatformUser.Create(
            "reviewer",
            "reviewer@example.com",
            "password-hash",
            PlatformUserRole.User | PlatformUserRole.Reviewer,
            IdGenerator);
        var listing = PluginListing.Create(
            "quantum.plugin.test",
            "Test",
            string.Empty,
            owner.Id,
            idGenerator: IdGenerator);
        var release = PluginRelease.Create(
            listing.Id,
            "1.0.0",
            ">=0.1.0",
            string.Empty,
            "quantum.plugin.test/1.0.0.zip",
            128,
            new string('a', 64),
            IdGenerator);

        Assert.True(PluginReleaseDownloadPolicy.CanDownload(listing, release, owner));
        Assert.True(PluginReleaseDownloadPolicy.CanDownload(listing, release, reviewer));
        Assert.False(PluginReleaseDownloadPolicy.CanDownload(listing, release, stranger));
        Assert.False(PluginReleaseDownloadPolicy.CanDownload(listing, release, null));
    }

    [Fact]
    public void PublishedRelease_IsPublicButRejectedReleaseIsNotDownloadable()
    {
        var owner = PlatformUser.Create("owner", "owner@example.com", "hash", idGenerator: IdGenerator);
        var reviewer = PlatformUser.Create(
            "reviewer",
            "reviewer@example.com",
            "hash",
            PlatformUserRole.Reviewer,
            IdGenerator);
        var listing = PluginListing.Create("quantum.plugin.test", "Test", string.Empty, owner.Id, idGenerator: IdGenerator);
        var published = PluginRelease.Create(listing.Id, "1.0.0", ">=0.1.0", string.Empty, "published.zip", 1, new string('a', 64), IdGenerator);
        var rejected = PluginRelease.Create(listing.Id, "1.0.1", ">=0.1.0", string.Empty, "rejected.zip", 1, new string('b', 64), IdGenerator);
        published.Review(PluginReleaseStatus.Published, reviewer.Id, null);
        rejected.Review(PluginReleaseStatus.Rejected, reviewer.Id, null);

        Assert.True(PluginReleaseDownloadPolicy.CanDownload(listing, published, null));
        Assert.False(PluginReleaseDownloadPolicy.CanDownload(listing, rejected, owner));
        Assert.False(PluginReleaseDownloadPolicy.CanDownload(listing, rejected, reviewer));
    }
}
