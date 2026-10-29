using System.Text.Json;

namespace Quantum.Platform.Tests;

public sealed class ReleasePublicationTests
{
    [Theory]
    [InlineData("1", "1", "Pending")]
    [InlineData("1", "2", "Pending")]
    [InlineData("1", "3", "Pending")]
    [InlineData("2", "3", "Published")]
    [InlineData("\"Published\"", "\"Approved\"", "Published")]
    [InlineData("\"Pending\"", "\"Running\"", "Pending")]
    public void OnlyPublishedMeansPublished(string status, string review, string expected)
    {
        using var json = JsonDocument.Parse($$"""{"status":{{status}},"automatedReviewStatus":{{review}}} """);
        Assert.Equal(expected, ReleasePublication.Evaluate(json.RootElement));
    }

    [Theory]
    [InlineData("3", "4")]
    [InlineData("1", "4")]
    [InlineData("1", "5")]
    [InlineData("1", "6")]
    [InlineData("99", "1")]
    [InlineData("1", "99")]
    [InlineData("\"Pending\"", "\"ManualReview\"")]
    public void RejectedManualFailedAndUnknownCannotSucceed(string status, string review)
    {
        using var json = JsonDocument.Parse($$"""{"status":{{status}},"automatedReviewStatus":{{review}}} """);
        Assert.Throws<QuantumCli.CliException>(() => ReleasePublication.Evaluate(json.RootElement));
    }
}
