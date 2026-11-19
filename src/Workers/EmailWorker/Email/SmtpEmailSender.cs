using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Shared.Messaging;

namespace EmailWorker.Email;

// Sends emails over SMTP with MailKit (Microsoft recommends MailKit instead of System.Net.Mail.SmtpClient).
// Turns every failure into an EmailSendException that says whether retrying can help.
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendOrderConfirmationAsync(OrderCreatedEvent order, CancellationToken cancellationToken)
    {
        MimeMessage message;
        try
        {
            message = OrderConfirmationEmail.Create(order, _options);
        }
        catch (ParseException ex)
        {
            throw new EmailSendException($"Invalid recipient address '{order.CustomerEmail}'", isPermanent: true, ex);
        }

        using var client = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, SecurityOption(), cancellationToken);

            if (!string.IsNullOrEmpty(_options.UserName))
                await client.AuthenticateAsync(_options.UserName, _options.Password ?? string.Empty, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            _logger.LogInformation("Confirmation email for order {OrderId} sent to {Recipient} via {Host}",
                order.OrderId, order.CustomerEmail, _options.Host);
        }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode >= 500)
        {
            // 5xx = the server refused for good (e.g. 550 mailbox unavailable): retrying cannot help.
            throw new EmailSendException($"SMTP rejected the email permanently ({(int)ex.StatusCode}): {ex.Message}", isPermanent: true, ex);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or AuthenticationException
                                       or ServiceNotConnectedException or SocketException or IOException or TimeoutException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Server down, timeout, 4xx "try later", or a login problem: worth retrying later.
            throw new EmailSendException($"SMTP temporarily unavailable: {ex.Message}", isPermanent: false, ex);
        }
    }

    private SecureSocketOptions SecurityOption() => _options.Security switch
    {
        "StartTls" => SecureSocketOptions.StartTls,
        "SslOnConnect" => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.None
    };
}
