using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Notifications.Decorators;
using Modules.Notifications.Interfaces;
using Modules.Notifications.Realtime;
using Modules.Notifications.Services;
using Shared.Messaging;

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

        services.AddSignalR();
        services.AddOptions<RabbitMqOptions>().BindConfiguration(RabbitMqOptions.SectionName);
        services.AddHostedService<OrderEventsConsumer>();

        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsHubs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<OrdersHub>("/hubs/orders",
            options => options.CloseOnAuthenticationExpiration = true);
        endpoints.MapHub<AdminOrdersHub>("/hubs/admin-orders",
            options => options.CloseOnAuthenticationExpiration = true);

        return endpoints;
    }
}