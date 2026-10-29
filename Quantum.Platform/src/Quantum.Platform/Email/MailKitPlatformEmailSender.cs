using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Quantum.Platform.Application;

namespace Quantum.Platform;

public sealed class MailKitPlatformEmailSender(
    IOptions<PlatformEmailOptions> options,
    ILogger<MailKitPlatformEmailSender> logger) : IPlatformEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogDebug("Platform email is disabled; skipped message to {Recipient}.", recipient);
            return;
        }

        var message = new MimeMessage
        {
            Subject = subject,
            Body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody()
        };
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));

        using var client = new SmtpClient
        {
            Timeout = checked(settings.RequestTimeoutSeconds * 1_000)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        try
        {
            await client.ConnectAsync(
                settings.SmtpHost,
                settings.SmtpPort,
                settings.EnableSsl,
                timeout.Token);
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, timeout.Token);
            }

            await client.SendAsync(message, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("SMTP email delivery timed out.", exception);
        }
    }
}
