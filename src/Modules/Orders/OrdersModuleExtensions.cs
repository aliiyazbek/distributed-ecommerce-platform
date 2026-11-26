using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Orders.Decorators;
using Modules.Orders.Interfaces;
using Modules.Orders.Persistence;
using Modules.Orders.Persistence.Outbox;
using Modules.Orders.Services;
using Modules.Orders.Integration.Protos;
using Shared.Data;
using Shared.Messaging;

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

        // Register the gRPC client for the Inventory Service (configurable for Docker networking)
        var inventoryUrl = configuration["GrpcServices:InventoryUrl"] ?? "http://127.0.0.1:50051";
        services.AddGrpcClient<InventoryService.InventoryServiceClient>("OrdersInventoryClient", options =>
        {
            options.Address = new Uri(inventoryUrl);
        });

        // Requirement 4: outbox publisher (background service) + its wake-up signal.
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<OutboxSignal>();
        services.AddHostedService<OutboxPublisher>();

        return services;
    }
}