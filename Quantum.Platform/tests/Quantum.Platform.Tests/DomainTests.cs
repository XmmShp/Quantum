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

    [Fact]
    public void AutomatedReview_ApprovesPendingReleaseAndRecordsMachineTask()
    {
        var release = PluginRelease.Create(
            PluginListingId.Of(1),
            "1.0.0",
            ">=0.1.0",
            string.Empty,
            "package.zip",
            128,
            new string('a', 64),
            IdGenerator);

        release.BeginAutomatedReview();
        release.RecordAutomatedReviewTask("agentfn-task-1");
        release.CompleteAutomatedReview(AutomatedReviewStatus.Approved, "No blocking findings.");

        Assert.Equal(PluginReleaseStatus.Published, release.Status);
        Assert.Equal(AutomatedReviewStatus.Approved, release.AutomatedReviewState);
        Assert.Equal("agentfn-task-1", release.AutomatedReviewTaskId);
        Assert.Null(release.ReviewedByUserId);
        Assert.Equal("No blocking findings.", release.ReviewNotes);
        Assert.Equal(1, release.AutomatedReviewAttempts);
    }

    [Fact]
    public void AutomatedReview_ManualDecisionLeavesReleasePending()
    {
        var release = PluginRelease.Create(
            PluginListingId.Of(1),
            "1.0.0",
            ">=0.1.0",
            string.Empty,
            "package.zip",
            128,
            new string('a', 64),
            IdGenerator);

        release.BeginAutomatedReview();
        release.RecordAutomatedReviewTask("agentfn-task-2");
        release.CompleteAutomatedReview(AutomatedReviewStatus.ManualReview, "Binary requires manual inspection.");

        Assert.Equal(PluginReleaseStatus.Pending, release.Status);
        Assert.Equal(AutomatedReviewStatus.ManualReview, release.AutomatedReviewState);
        Assert.Null(release.ReviewedAtUtc);
    }

    [Fact]
    public void RegistrationVerification_EnforcesResendExpiryAndAttemptLimits()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));
        var verification = RegistrationEmailVerification.Create(
            "Developer@Example.com",
            "stored-code-hash",
            IdGenerator,
            timeProvider);

        Assert.Equal("developer@example.com", verification.Email);
        Assert.False(verification.CanResend(timeProvider));
        Assert.True(verification.CanVerify(timeProvider));

        timeProvider.Advance(RegistrationEmailVerification.ResendInterval);
        Assert.True(verification.CanResend(timeProvider));

        for (var attempt = 0; attempt < RegistrationEmailVerification.MaximumFailedAttempts; attempt++)
        {
            verification.RecordFailedAttempt();
        }

        Assert.False(verification.CanVerify(timeProvider));

        verification.ReplaceCode("replacement-hash", timeProvider);
        Assert.Equal(0, verification.FailedAttempts);
        timeProvider.Advance(RegistrationEmailVerification.Lifetime + TimeSpan.FromSeconds(1));
        Assert.False(verification.CanVerify(timeProvider));
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData(" 654321 ", "654321")]
    public void RegistrationVerification_NormalizesSixDigitCodes(string value, string expected)
        => Assert.Equal(expected, RegistrationEmailVerification.NormalizeCode(value));

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void RegistrationVerification_RejectsInvalidCodes(string value)
        => Assert.Throws<ArgumentException>(() => RegistrationEmailVerification.NormalizeCode(value));

    [Fact]
    public void RegistrationRoles_GrantEveryRoleOnlyToTheFirstUser()
    {
        Assert.Equal(
            PlatformUserRole.User | PlatformUserRole.Developer | PlatformUserRole.Reviewer | PlatformUserRole.Admin,
            PlatformUser.RegistrationRoles(isFirstUser: true));
        Assert.Equal(
            PlatformUserRole.User | PlatformUserRole.Developer,
            PlatformUser.RegistrationRoles(isFirstUser: false));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan value) => utcNow += value;
    }
}
