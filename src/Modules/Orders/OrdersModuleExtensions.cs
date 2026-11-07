using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Orders.Decorators;
using Modules.Orders.Interfaces;
using Modules.Orders.Services;
using Modules.Orders.Integration.Protos;

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
        // This tells .NET where the future Node.js server will be hosted
        services.AddGrpcClient<InventoryService.InventoryServiceClient>("OrdersInventoryClient",options =>
        {
            options.Address = new Uri("http://127.0.0.1:50051"); 
        });

        return services;
    }
}
