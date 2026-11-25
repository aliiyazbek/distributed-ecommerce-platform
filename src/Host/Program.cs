using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Asp.Versioning;
using Modules.Users;
using Modules.Users.Persistence;
using Modules.Products;
using Modules.Orders;
using Modules.Notifications;
using Shared;
using Modules.Users.Interfaces;
using Shared.Caching;
using Shared.Data;
using Shared.Http;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Users always use the primary (login and the duplicate-email check must see the latest data).
// Products and Orders register their own primary + read-replica contexts in their modules.
builder.Services.AddDbContext<UsersDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Users")));

// 2. Swagger & Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3. Unified Error Handling (Requirement 1)
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// 4. API Versioning (Requirement 1)
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

// 4b. Forwarded Headers for Nginx Reverse Proxy (Requirement 5)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2; // Accepts Nginx + one upstream hop (allows multi-IP simulation behind Docker)
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// 5. JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Key"] ?? "super_secret_key_that_must_be_at_least_32_characters_long_for_hs256!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// 6. Fallback Local Rate Limiting (RedisAuthRateLimiterMiddleware enforces the global limit across instances)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Per-request record of cache hits/misses, read by CacheStatusHeaderMiddleware
builder.Services.AddScoped<CacheStatus>();

// One shared "is Redis down?" switch for the whole app
var cacheCooldown = TimeSpan.FromSeconds(builder.Configuration.GetValue("Caching:FailureCooldownSeconds", 30));
builder.Services.AddSingleton(new CacheCircuitBreaker(cacheCooldown));

// 6b. Redis (Requirements 3 & 5)
var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379,abortConnect=false";
var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
    options.InstanceName = "ecommerce:";
});

// 6c. Read replica routing (Requirement 3)
builder.Services.AddReadReplicaRouting(builder.Configuration);

// 7. Register Modules & MediatR
builder.Services.AddUsersModule();
builder.Services.AddProductsModule(builder.Configuration, connectionString);
builder.Services.AddOrdersModule(builder.Configuration, connectionString);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(AppDomain.CurrentDomain.GetAssemblies()));

var app = builder.Build();

// 8. Middleware Pipeline (Order is strictly critical)
app.UseForwardedHeaders(); // Must run first so client IP reflects the real caller behind Nginx

// Expose instance name for load-distribution proof (Requirement 5)
var instanceId = Environment.MachineName;
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Instance-Id"] = instanceId;
        return Task.CompletedTask;
    });
    await next();
});

app.UseMiddleware<CacheStatusHeaderMiddleware>(); // X-Cache: HIT | MISS | BYPASS
app.UseMiddleware<ReadSourceHeaderMiddleware>();  // X-Db-Read: REPLICA | PRIMARY

// Apply Security Headers (Requirement 1)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    await next();
});

app.UseExceptionHandler(); // Catches all errors and routes them to GlobalExceptionHandler

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORTS")))
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseMiddleware<RedisAuthRateLimiterMiddleware>(); // Shared auth rate limit across all 3 API instances
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapNotificationsHubs();

app.Run();