using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Quantum.Platform.Tests;

public sealed class MailKitPlatformEmailSenderTests
{
    [Fact]
    public async Task SendAsync_DeliversTextAndHtmlThroughSmtp()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedMessage = ReceiveMessageAsync(listener, timeout.Token);
        var sender = new MailKitPlatformEmailSender(
            Options.Create(new PlatformEmailOptions
            {
                Enabled = true,
                SmtpHost = IPAddress.Loopback.ToString(),
                SmtpPort = port,
                EnableSsl = false,
                FromAddress = "market@example.com",
                FromName = "Quantum Platform",
                RequestTimeoutSeconds = 5
            }),
            NullLogger<MailKitPlatformEmailSender>.Instance);

        await sender.SendAsync(
            "developer@example.com",
            "Review result",
            "Published",
            "<p>Published</p>",
            timeout.Token);
        var rawMessage = await receivedMessage;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawMessage));
        var message = await MimeMessage.LoadAsync(stream, timeout.Token);

        Assert.Equal("market@example.com", message.From.Mailboxes.Single().Address);
        Assert.Equal("developer@example.com", message.To.Mailboxes.Single().Address);
        Assert.Equal("Review result", message.Subject);
        Assert.Equal("Published", message.TextBody?.Trim());
        Assert.Equal("<p>Published</p>", message.HtmlBody?.Trim());
    }

    private static async Task<string> ReceiveMessageAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync("220 localhost ESMTP ready");
        string? received = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } command)
        {
            if (command.StartsWith("EHLO ", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("HELO ", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 localhost");
            }
            else if (command.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 OK");
            }
            else if (command == "DATA")
            {
                await writer.WriteLineAsync("354 End data");
                var message = new StringBuilder();
                while (await reader.ReadLineAsync(cancellationToken) is { } line && line != ".")
                {
                    message.AppendLine(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);
                }

                received = message.ToString();
                await writer.WriteLineAsync("250 Queued");
            }
            else if (command == "QUIT")
            {
                await writer.WriteLineAsync("221 Bye");
                return received ?? throw new InvalidOperationException("No email data was received.");
            }
            else
            {
                await writer.WriteLineAsync("250 OK");
            }
        }

        return received ?? throw new InvalidOperationException("SMTP disconnected before DATA.");
    }
}
