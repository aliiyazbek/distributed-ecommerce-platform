using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Modules.Users;using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Modules.Users.Persistence;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
var connectionString = builder.Configuration.GetConnectionString("Database") 
                       ?? throw new InvalidOperationException("Connection string 'Database' not found.");

builder.Services.AddDbContext<UsersDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. JWT Authentication
var jwtSecret = "super_secret_key_that_must_be_at_least_32_characters_long_for_hs256!";
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

// 3. Rate Limiting (Requirement 1: Prevent Brute-Force Attacks)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth-limit", limitOptions =>
    {
        limitOptions.PermitLimit = 5; // Max 5 attempts
        limitOptions.Window = TimeSpan.FromSeconds(10); // Per 10 seconds
        limitOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limitOptions.QueueLimit = 0; // Drop requests immediately if over limit
    });
});

// 4. Register Modules & MediatR
builder.Services.AddUsersModule(); // Your clean extension method
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(AppDomain.CurrentDomain.GetAssemblies()));

builder.Services.AddControllers();

var app = builder.Build();

// 5. Middleware Pipeline (Order is strictly critical)
app.UseHttpsRedirection();

app.UseRateLimiter(); // Must be before routing/auth
app.UseAuthentication(); // Must be before authorization
app.UseAuthorization();

app.MapControllers().RequireRateLimiting("auth-limit"); // Apply rate limit to endpoints

app.Run();