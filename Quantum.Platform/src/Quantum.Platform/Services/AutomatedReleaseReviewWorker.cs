using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Domain;
using Quantum.Platform.Application;
using Quantum.Platform.Domain;

namespace Quantum.Platform;

public sealed class AutomatedReleaseReviewWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AgentFnAutomatedReviewOptions> options,
    ILogger<AutomatedReleaseReviewWorker> logger) : BackgroundService
{
    private readonly AgentFnAutomatedReviewOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsWorkerEnabled)
        {
            logger.LogInformation("Automated plugin release review is disabled.");
            return;
        }

        logger.LogInformation(
            options.DevelopmentAutoApprove
                ? "Development-only automatic plugin release approval is enabled."
                : "AgentFn automated plugin release review is enabled.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The automated release review worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var releases = scope.ServiceProvider.GetRequiredService<IRepository<PluginRelease>>();
        var listings = scope.ServiceProvider.GetRequiredService<IRepository<PluginListing>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDbContext>();
        var packageStore = scope.ServiceProvider.GetRequiredService<IPluginPackageStore>();
        var reviewer = scope.ServiceProvider.GetRequiredService<IPluginReleaseAutomatedReviewer>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var release = await releases
            .Where(candidate => candidate.Status == PluginReleaseStatus.Pending &&
                (candidate.AutomatedReviewState == AutomatedReviewStatus.Queued ||
                 candidate.AutomatedReviewState == AutomatedReviewStatus.Running ||
                 candidate.AutomatedReviewState == AutomatedReviewStatus.Failed &&
                    candidate.AutomatedReviewAttempts < options.MaxAttempts))
            .OrderBy(static candidate => candidate.UploadedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (release is null)
        {
            return false;
        }

        var listing = await listings.AsNoTracking()
            .Where(candidate => candidate.Id == release.ListingId)
            .SingleAsync(cancellationToken);
        try
        {
            if (release.AutomatedReviewState is AutomatedReviewStatus.Queued or AutomatedReviewStatus.Failed)
            {
                release.BeginAutomatedReview(timeProvider);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var taskId = release.AutomatedReviewTaskId;
            if (string.IsNullOrWhiteSpace(taskId))
            {
                var package = await packageStore.ReadAsync(release.PackagePath, cancellationToken);
                taskId = await reviewer.StartAsync(listing, release, package, cancellationToken);
                release.RecordAutomatedReviewTask(taskId);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var result = await reviewer.WaitAsync(taskId, cancellationToken);
            var reviewStatus = result.Decision switch
            {
                AutomatedPluginReviewDecision.Approve => AutomatedReviewStatus.Approved,
                AutomatedPluginReviewDecision.Reject => AutomatedReviewStatus.Rejected,
                _ => AutomatedReviewStatus.ManualReview
            };
            release.CompleteAutomatedReview(reviewStatus, result.Summary, timeProvider);
            await auditWriter.WriteAsync(
                reviewStatus switch
                {
                    AutomatedReviewStatus.Approved => "plugin.release_auto_published",
                    AutomatedReviewStatus.Rejected => "plugin.release_auto_rejected",
                    _ => "plugin.release_manual_review_requested"
                },
                null,
                $"AgentFn task '{taskId}' completed with '{reviewStatus}'. {result.Summary}",
                listing.Id,
                release.Id,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Automated review completed for release {ReleaseId} with {ReviewStatus}.",
                release.Id,
                reviewStatus);
        }
        catch (NOF.Application.DbUpdateConcurrencyException)
        {
            logger.LogInformation("Release {ReleaseId} changed while automated review was running; stale result was discarded.", release.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                release.FailAutomatedReview("Automated review could not complete. A retry or manual review is required.", timeProvider);
                await auditWriter.WriteAsync(
                    "plugin.release_auto_review_failed",
                    null,
                    $"Automated review attempt {release.AutomatedReviewAttempts} failed for AgentFn task '{release.AutomatedReviewTaskId ?? "not_created"}'.",
                    listing.Id,
                    release.Id,
                    cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (NOF.Application.DbUpdateConcurrencyException)
            {
                logger.LogInformation("Release {ReleaseId} changed while recording an automated review failure.", release.Id);
            }

            logger.LogWarning(exception, "Automated review attempt failed for release {ReleaseId}.", release.Id);
        }

        return true;
    }
}
