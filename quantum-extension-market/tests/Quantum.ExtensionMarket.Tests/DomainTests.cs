using Quantum.ExtensionMarket.Application;
using Quantum.ExtensionMarket.Domain;
using NOF.Domain;

namespace Quantum.ExtensionMarket.Tests;

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
            MarketUserId.Of(42),
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

        release.Review(PluginReleaseStatus.Published, MarketUserId.Of(2), "Approved");
        release.RecordDownload();

        Assert.Equal(PluginReleaseStatus.Published, release.Status);
        Assert.Equal(1, release.DownloadCount);
    }
}
