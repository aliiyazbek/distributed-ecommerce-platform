using Microsoft.Extensions.Logging;
using Modules.Notifications.Interfaces;
using System.Diagnostics;

namespace Modules.Notifications.Decorators;

public class LoggingNotificationServiceDecorator : INotificationService
{
    private readonly INotificationService _inner;
    private readonly ILogger<LoggingNotificationServiceDecorator> _logger;

    public LoggingNotificationServiceDecorator(INotificationService inner, ILogger<LoggingNotificationServiceDecorator> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        _logger.LogInformation("Sending email from nannrash81@gmail.com to {ToEmail} with subject '{Subject}'", to, subject);
        var stopwatch = Stopwatch.StartNew();
        
        await _inner.SendEmailAsync(to, subject, body);
        
        stopwatch.Stop();
        _logger.LogInformation("Email sent successfully to {ToEmail} in {ElapsedMilliseconds}ms", to, stopwatch.ElapsedMilliseconds);
    }
}