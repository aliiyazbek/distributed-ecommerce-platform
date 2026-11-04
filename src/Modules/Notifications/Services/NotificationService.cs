using System.Net;
using System.Net.Mail;
using Modules.Notifications.Interfaces;

namespace Modules.Notifications.Services;

public class NotificationService : INotificationService
{
    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var senderEmail = "nannrash81@gmail.com";
        // Paste your 16-character Google App Password here (NO SPACES):
        var appPassword = "fcka ejvl ufvv qtpv"; 

        // Connects to Google's real SMTP server with SSL enabled
        using var client = new SmtpClient("smtp.gmail.com", 587)
        {
            Credentials = new NetworkCredential(senderEmail, appPassword),
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        
        var mailMessage = new MailMessage(
            from: senderEmail,
            to: to,
            subject: subject,
            body: body
        );

        await client.SendMailAsync(mailMessage);
    }
}