using MediatR;
using Microsoft.Extensions.Logging;
using Modules.Notifications.Interfaces;
using Shared.Events;

namespace Modules.Notifications.Handlers;

public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedIntegrationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<ProductCreatedEventHandler> _logger;

    public ProductCreatedEventHandler(INotificationService notificationService, ILogger<ProductCreatedEventHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Handle(ProductCreatedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Event Caught in Notifications Module: New product '{ProductName}' added.", notification.ProductName);

        var subject = $"New Product Alert: {notification.ProductName}!";
        var body = $"Great news! We just added a new product to our {notification.Category} catalog: {notification.ProductName} for ${notification.Price}. Check it out now!";

        // Sending to your actual Gmail inbox
        var targetEmail = "nadero.9alrasheed@gmail.com"; 

        await _notificationService.SendEmailAsync(targetEmail, subject, body);
    }
}