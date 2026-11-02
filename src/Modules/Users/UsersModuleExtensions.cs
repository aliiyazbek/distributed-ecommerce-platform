using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Users.Decorators;
using Modules.Users.Interfaces;
using Modules.Users.Persistence;
using Modules.Users.Persistence.Repositories;
using Modules.Users.Services;

namespace Modules.Users;

public static class UsersModuleExtensions
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        // 1. Register the concrete core service
        services.AddScoped<AuthService>();

        // 2. Register the Decorator to wrap the core service for logging
        services.AddScoped<IAuthService>(provider =>
        {
            var baseService = provider.GetRequiredService<AuthService>();
            var logger = provider.GetRequiredService<ILogger<LoggingAuthServiceDecorator>>();
            return new LoggingAuthServiceDecorator(baseService, logger);
        });

        // 3. Register FluentValidation
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<Validators.RegisterRequestValidator>();

        // 4. Register the Database Abstractions (Persistence Ignorance)
        services.AddScoped<IUserRepository, UserRepository>();
        
        // Register IUnitOfWork using the factory delegate tied to the DbContext
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UsersDbContext>());

        return services;
    }
}