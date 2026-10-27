namespace Quantum.Platform;

public sealed class S3AttachmentCleanup(IQuantumObjectStore objects, ILogger<S3AttachmentCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await ((S3ObjectStore)objects).DeleteExpiredMobileObjectsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning("S3 attachment expiration will retry; failure type: {FailureType}", exception.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
