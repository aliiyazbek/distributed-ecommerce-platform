using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Http;

namespace Shared.Data;

public static class ReadReplicaServiceCollectionExtensions
{
    // Registers the pieces modules use to route reads to the replica (Requirement 3).
    // Needs an IConnectionMultiplexer (Redis) and a CacheCircuitBreaker registered by the host.
    public static IServiceCollection AddReadReplicaRouting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReadReplicaOptions>(configuration.GetSection(ReadReplicaOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddSingleton<ReplicaHealth>();
        services.AddSingleton<IWritePositionStore, RedisWritePositionStore>();
        services.AddScoped<ReadSourceStatus>();
        services.AddScoped<ReadRouter>();

        return services;
    }

    // The replica connection string, or the primary's when no replica is configured
    // (reads then simply never use it: see ReadReplicaOptions.Enabled).
    public static string GetReadReplicaConnectionString(this IConfiguration configuration, string primaryConnectionString) =>
        configuration.GetConnectionString("ReadReplicaConnection") is { Length: > 0 } replica
            ? replica
            : primaryConnectionString;
}
