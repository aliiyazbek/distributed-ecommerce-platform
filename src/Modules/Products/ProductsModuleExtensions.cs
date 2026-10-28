using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Products.Decorators;
using Modules.Products.Interfaces;
using Modules.Products.Persistence;
using Modules.Products.Services;

namespace Modules.Products;

public static class ProductsModuleExtensions
{
    public static IServiceCollection AddProductsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ProductsDbContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Products")));

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