using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Orders.Decorators;
using Modules.Orders.Interfaces;
using Modules.Orders.Persistence;
using Modules.Orders.Services;
using Modules.Orders.Integration.Protos;
using Shared.Data;

namespace Modules.Orders;

public static class OrdersModuleExtensions
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        // PRIMARY: all writes. The interceptor records each committed write's WAL position under
        // the writer's scope, so that user's next reads never see the replica before it has the write.
        services.AddDbContext<OrdersDbContext>((sp, options) => options
            .UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Orders"))
            .AddInterceptors(WritePositionInterceptor.Create(sp, connectionString)));

        // READ REPLICA: read-only, no change tracking (Requirement 3).
        services.AddDbContext<OrdersReadDbContext>(options => options
            .UseNpgsql(configuration.GetReadReplicaConnectionString(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

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
