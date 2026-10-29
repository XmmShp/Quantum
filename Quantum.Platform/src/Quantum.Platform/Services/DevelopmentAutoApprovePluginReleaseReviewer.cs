using Quantum.Platform.Application;
using Quantum.Platform.Domain;

namespace Quantum.Platform;

public sealed class DevelopmentAutoApprovePluginReleaseReviewer : IPluginReleaseAutomatedReviewer
{
    public Task<string> StartAsync(
        PluginListing listing,
        PluginRelease release,
        byte[] packageArchive,
        CancellationToken cancellationToken)
        => Task.FromResult($"development-auto-approve-{(long)release.Id}-{release.PackageSha256[..12]}");

    public Task<AutomatedPluginReviewResult> WaitAsync(
        string taskId,
        CancellationToken cancellationToken)
        => Task.FromResult(new AutomatedPluginReviewResult(
            taskId,
            AutomatedPluginReviewDecision.Approve,
            "Automatically approved by the Development-only local reviewer."));
}
