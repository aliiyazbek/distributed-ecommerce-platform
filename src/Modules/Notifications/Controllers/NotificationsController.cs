using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Modules.Notifications.Contracts;
using Modules.Notifications.Interfaces;

namespace Modules.Notifications.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendEmail([FromForm] SendEmailRequest request)
    {
        // Defaults to your target email if the user doesn't provide one in Postman
        var targetEmail = string.IsNullOrWhiteSpace(request.ToEmail) 
            ? "nadero.9alrasheed@gmail.com" 
            : request.ToEmail;

        await _notificationService.SendEmailAsync(targetEmail, request.Subject, request.Message);
        
        return Ok(new { 
            Message = "Email dispatched successfully.",
            Sender = "nannrash81@gmail.com",
            Recipient = targetEmail 
        });
    }
}