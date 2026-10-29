using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Orders.Decorators;
using Modules.Orders.Interfaces;
using Modules.Orders.Services;

namespace Modules.Orders;

public static class OrdersModuleExtensions
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddScoped<OrderService>();

        services.AddScoped<IOrderService>(provider =>
        {
            var baseService = provider.GetRequiredService<OrderService>();
            var logger = provider.GetRequiredService<ILogger<LoggingOrderServiceDecorator>>();
            return new LoggingOrderServiceDecorator(baseService, logger);
        });

        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<Validators.CreateOrderRequestValidator>();

        return services;
    }
}
