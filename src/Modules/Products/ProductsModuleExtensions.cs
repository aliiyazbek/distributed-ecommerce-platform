using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Products.Caching;
using Modules.Products.Decorators;
using Modules.Products.Interfaces;
using Modules.Products.Persistence;
using Modules.Products.Services;
using Modules.Orders.Integration.Protos; // Add the namespace generated from your proto file
using Modules.Products.Persistence.Repositories;
using Shared.Caching;
using Shared.Data;


namespace Modules.Products;

public static class ProductsModuleExtensions
{
    public static IServiceCollection AddProductsModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        // PRIMARY: all writes. The interceptor records each committed write's WAL position under
        // the writer's scope and the shared catalog scope (read-your-writes on the replica).
        services.AddDbContext<ProductsDbContext>((sp, options) => options
            .UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Products"))
            .AddInterceptors(WritePositionInterceptor.Create(sp, connectionString, ProductsDbContext.CatalogScope)));

        // READ REPLICA: read-only, no change tracking (Requirement 3).
        services.AddDbContext<ProductsReadDbContext>(options => options
            .UseNpgsql(configuration.GetReadReplicaConnectionString(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        // NEW: Register the gRPC client for the Inventory Service
        services.AddGrpcClient<InventoryService.InventoryServiceClient>("ProductsInventoryClient", options =>
        {
            options.Address = new Uri("http://127.0.0.1:50051"); 
        });

        // Cache settings (options pattern)
        var cacheSection = configuration.GetSection(ProductCacheOptions.SectionName);
        services.Configure<ProductCacheOptions>(cacheSection);
        var cacheOptions = cacheSection.Get<ProductCacheOptions>() ?? new ProductCacheOptions();

        // Data access: the real repository is registered as its concrete type...
        services.AddScoped<ProductRepository>();

        // ...and the interface returns it wrapped in the caching decorator, or bare when caching is off.
        if (cacheOptions.Enabled)
        {
            services.AddScoped<IProductRepository>(provider => new CachingProductRepositoryDecorator(
                provider.GetRequiredService<ProductRepository>(),
                provider.GetRequiredService<IDistributedCache>(),
                provider.GetRequiredService<CacheStatus>(),
                provider.GetRequiredService<CacheCircuitBreaker>(),
                provider.GetRequiredService<IOptions<ProductCacheOptions>>(),
                provider.GetRequiredService<ILogger<CachingProductRepositoryDecorator>>()));
        }
        else
        {
            services.AddScoped<IProductRepository>(provider => provider.GetRequiredService<ProductRepository>());
        }
        
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