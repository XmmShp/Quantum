using Quantum.Platform.Application;

namespace Quantum.Platform;

public sealed class DevelopmentLoggingPlatformEmailSender(
    ILogger<DevelopmentLoggingPlatformEmailSender> logger) : IPlatformEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Development email to {Recipient}. Subject: {Subject}. Body: {TextBody}",
            recipient,
            subject,
            textBody);
        return Task.CompletedTask;
    }
}
