using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Products.Caching;
using Modules.Products.Decorators;
using Modules.Products.Interfaces;
using Modules.Products.Persistence;
using Modules.Products.Services;
using Modules.Orders.Integration.Protos; // Add the namespace generated from your proto file



namespace Modules.Products;

public static class ProductsModuleExtensions
{
    public static IServiceCollection AddProductsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ProductsDbContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Products")));

        // NEW: Register the gRPC client for the Inventory Service
        services.AddGrpcClient<InventoryService.InventoryServiceClient>("ProductsInventoryClient", options =>
        {
            options.Address = new Uri("http://127.0.0.1:50051"); 
        });

        services.AddHttpContextAccessor();
        services.AddScoped<IProductCache, ProductCache>();

        // Register the core service as its concrete type
        services.AddScoped<ProductService>();
        
        // Register the interface to return the Decorator, wrapping the inner core service
        services.AddScoped<IProductService>(provider => 
        {
            var innerService = provider.GetRequiredService<ProductService>();
            var logger = provider.GetRequiredService<ILogger<LoggingProductServiceDecorator>>();
            return new LoggingProductServiceDecorator(innerService, logger);
        });

        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<ProductsDbContext>();

        return services;
    }
}