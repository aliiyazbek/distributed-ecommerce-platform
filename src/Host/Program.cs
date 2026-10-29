using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Asp.Versioning;
using Modules.Users;
using Modules.Users.Persistence;
using Modules.Products.Persistence;
using Modules.Products;
using Modules.Orders;
using Modules.Orders.Persistence;
using Shared;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<UsersDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Users")));

builder.Services.AddDbContext<ProductsDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Products")));

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("Modules.Orders")));

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
    });
builder.Services.AddAuthorization();

// 6. Rate Limiting (Partitioned by IP Address)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    options.AddPolicy("auth-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Use the client's IP address as the unique partition key
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, // Max 5 attempts per IP
                Window = TimeSpan.FromSeconds(10), // Per 10 seconds
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});
// 7. Register Modules & MediatR
builder.Services.AddUsersModule(); 
builder.Services.AddProductsModule(connectionString); // <-- Make sure this line exists!
builder.Services.AddOrdersModule();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(AppDomain.CurrentDomain.GetAssemblies()));
var app = builder.Build();

// 8. Middleware Pipeline (Order is strictly critical)
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

app.UseHttpsRedirection();

app.UseStaticFiles(); // Added here so static files are served efficiently

app.UseRateLimiter(); // Must be before routing/auth
app.UseAuthentication(); // Must be before authorization
app.UseAuthorization();

app.MapControllers();

app.Run();