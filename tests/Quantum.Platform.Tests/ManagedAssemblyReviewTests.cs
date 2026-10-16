using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NOF.Domain;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Tests;

public sealed class ManagedAssemblyReviewTests
{
    [Fact]
    public void ReadsActualIlWithoutExecutingStaticInitializers()
    {
        var bytes = File.ReadAllBytes(typeof(ManagedReviewFixture.InspectionFixture).Assembly.Location);
        var code = ManagedAssemblyReview.Inspect("fixture.dll", bytes, default);
        Assert.Contains("https://inspection-fixture.invalid/visible-in-il", code);
        Assert.Contains("Plugin code must never execute", code);
        Assert.Contains("Raw resource string inspected without object deserialization", code);
        Assert.Contains(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), code);
    }

    [Fact]
    public void UnsupportedBinariesAndLimitsStayUninspectable()
    {
        Assert.Throws<BadImageFormatException>(() => ManagedAssemblyReview.Inspect("native.dll", [1,2,3], default));
        var options = new AgentFnAutomatedReviewOptions { MaxReviewFileBytes = 1 };
        using var zip = Workspace(options);
        using var inventory = JsonDocument.Parse(zip.GetEntry("package-inventory.json")!.Open());
        Assert.All(inventory.RootElement.EnumerateArray(), item => Assert.False(item.GetProperty("included").GetBoolean()));
        Assert.Null(zip.GetEntry("assembly-inspection/fixture.dll.cs"));
    }

    [Fact]
    public void WorkspaceLinksDerivedEvidenceToOriginalAssembly()
    {
        using var zip = Workspace(new());
        Assert.NotNull(zip.GetEntry("assembly-inspection/fixture.dll.cs"));
        using var inspection = JsonDocument.Parse(zip.GetEntry("assembly-inspection.json")!.Open());
        var item = inspection.RootElement.GetProperty("assemblies")[0];
        Assert.Equal("decompiled", item.GetProperty("status").GetString());
        Assert.Equal("fixture.dll", item.GetProperty("assemblyPath").GetString());
        Assert.Equal(64, item.GetProperty("sha256").GetString()!.Length);
    }

    private static ZipArchive Workspace(AgentFnAutomatedReviewOptions options)
    {
        using var source = new MemoryStream();
        using (var zip = new ZipArchive(source, ZipArchiveMode.Create, true))
        {
            using var output = zip.CreateEntry("fixture.dll").Open();
            output.Write(File.ReadAllBytes(typeof(ManagedReviewFixture.InspectionFixture).Assembly.Location));
        }
        var ids = new SnowflakeIdGenerator();
        var listing = PluginListing.Create("quantum.plugin.test", "Test", "Test", PlatformUserId.Of(1), [], ids);
        var release = PluginRelease.Create(listing.Id, "1.0.0", ">=0.1.0", "", "test.zip", 1, new string('a',64), ids);
        var reviewer = new AgentFnPluginReleaseAutomatedReviewer(Options.Create(options));
        return new ZipArchive(new MemoryStream(reviewer.BuildWorkspaceArchive(listing, release, source.ToArray())), ZipArchiveMode.Read);
    }

    [Fact]
    public void RetryCannotRewritePublishedOrRejectedDecision()
    {
        var ids = new SnowflakeIdGenerator();
        var release = PluginRelease.Create(PluginListingId.Of(1), "1.0.0", ">=0.1.0", "", "test.zip", 1, new string('a',64), ids);
        Assert.Throws<InvalidOperationException>(release.QueueAutomatedReview);
        release.BeginAutomatedReview();
        release.RecordAutomatedReviewTask("task-1");
        release.CompleteAutomatedReview(AutomatedReviewStatus.ManualReview, "Missing inspection");
        release.QueueAutomatedReview();
        Assert.Equal(AutomatedReviewStatus.Queued, release.AutomatedReviewState);
        Assert.Null(release.AutomatedReviewTaskId);
        Assert.Equal(new string('a',64), release.PackageSha256);
        release.BeginAutomatedReview();
        release.CompleteAutomatedReview(AutomatedReviewStatus.Approved, "Reviewed");
        Assert.Throws<InvalidOperationException>(release.QueueAutomatedReview);
    }
}
