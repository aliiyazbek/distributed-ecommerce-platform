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

    private const string JwtSecret = "super_secret_key_that_must_be_at_least_32_characters_long_for_hs256!";

    public AuthService(UsersDbContext dbContext, IPublisher publisher)
    {
        _dbContext = dbContext;
        _publisher = publisher;
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> RegisterAsync(RegisterRequest request)
    {
        // 1. Check for existing user
        if (await _dbContext.Users.AnyAsync(u => u.Email == request.Email))
            return (false, null, "Email is already registered.");

        // 2. Hash the password
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
        
        // 3. Create the Aggregate Root
        // BEST PRACTICE: Public registration defaults to Role 2 (Customer).
        int assignedRoleId = 2; 
        var user = new User(request.Email, hashedPassword, assignedRoleId);
        
        // 4. Factory Logic: Attach the correct Profile based on the Role
        if (assignedRoleId == 2)
        {
            var customerProfile = new CustomerProfile(user.Id);
            user.SetCustomerProfile(customerProfile);
        }
        else if (assignedRoleId == 1)
        {
            var adminProfile = new AdminProfile(user.Id, "General IT");
            user.SetAdminProfile(adminProfile);
        }
        
        // 5. Atomic Transaction / Unit of Work
        // EF Core Change Tracker sees the attached profile and queues both for insertion.
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // 6. Explicit Loading for the JWT
        await _dbContext.Entry(user).Reference(u => u.Role).LoadAsync();

        // 7. Publish integration event
        await _publisher.Publish(new Shared.Events.UserRegisteredIntegrationEvent(user.Id, user.Email));

        // 8. Generate JWT
        var token = GenerateJwtToken(user);
        
        return (true, new AuthResponse(user.Id, user.Email, token), null);
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> LoginAsync(LoginRequest request)
    {
        // 1. Retrieve the user AND their Role via SQL JOIN
        var user = await _dbContext.Users
            .Include(u => u.Role) 
            .SingleOrDefaultAsync(u => u.Email == request.Email);
        
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
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.Name) 
            }),
            Expires = DateTime.UtcNow.AddHours(2),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}