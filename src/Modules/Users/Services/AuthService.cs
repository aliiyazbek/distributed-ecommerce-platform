using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Modules.Users.Contracts;
using Modules.Users.Entities;
using Modules.Users.Interfaces;
using Modules.Users.Persistence;

namespace Modules.Users.Services;

public class AuthService : IAuthService
{
    private readonly UsersDbContext _dbContext;
    private readonly IPublisher _publisher;

    // In a production environment, this secret must be stored in appsettings.json, Environment Variables, or Azure Key Vault.
    // For this university project running locally in Docker, a hardcoded constant is acceptable for now.
    private const string JwtSecret = "super_secret_key_that_must_be_at_least_32_characters_long_for_hs256!";

    public AuthService(UsersDbContext dbContext, IPublisher publisher)
    {
        _dbContext = dbContext;
        _publisher = publisher;
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> RegisterAsync(RegisterRequest request)
    {
        // 1. Check for existing user to prevent duplicate emails
        // Use AnyAsync instead of retrieving the entire record. 
        // This translates to a highly optimized SQL 'SELECT EXISTS (...)' query,
        // minimizing database I/O, network bandwidth, and application memory allocation.
        if (await _dbContext.Users.AnyAsync(u => u.Email == request.Email))
            return (false, null, "Email is already registered.");

        // 2. Hash the password using BCrypt
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
        
        // 3. Create and save the domain entity
        var user = new User(request.Email, hashedPassword);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // 4. Publish the integration event for the Notifications module
        await _publisher.Publish(new Shared.Events.UserRegisteredIntegrationEvent(user.Id, user.Email));

        // 5. Generate the JWT
        var token = GenerateJwtToken(user);
        
        return (true, new AuthResponse(user.Id, user.Email, token), null);
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> LoginAsync(LoginRequest request)
    {
        // 1. Retrieve the user
        // Use SingleOrDefaultAsync to enforce strict data integrity on unique fields.
        // This translates to a SQL 'LIMIT 2' query—if the database somehow finds a second 
        // user with the same email, it intentionally throws an exception rather than 
        // silently logging in the wrong person.
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
        
        // 2. Verify existence and password match
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return (false, null, "Invalid email or password.");

        // 3. Generate the JWT
        var token = GenerateJwtToken(user);
        
        return (true, new AuthResponse(user.Id, user.Email, token), null);
    }

    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(JwtSecret);
        
        // The Claims describe the authenticated user identity and their Role
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            }),
            Expires = DateTime.UtcNow.AddHours(2),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
    
}