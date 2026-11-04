using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Notifications.Decorators;
using Modules.Notifications.Interfaces;
using Modules.Notifications.Services;

namespace Modules.Notifications;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<NotificationService>();
        
        services.AddScoped<INotificationService>(provider => 
        {
            var innerService = provider.GetRequiredService<NotificationService>();
            var logger = provider.GetRequiredService<ILogger<LoggingNotificationServiceDecorator>>();
            return new LoggingNotificationServiceDecorator(innerService, logger);
        });

        return services;
    }
}