using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.IdentityModel.Tokens;
using Modules.Users.Contracts;
using Modules.Users.Entities;
using Modules.Users.Interfaces;

namespace Modules.Users.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    private const string JwtSecret = "super_secret_key_that_must_be_at_least_32_characters_long_for_hs256!";

    public AuthService(IUserRepository userRepository, IUnitOfWork unitOfWork, IPublisher publisher)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> RegisterAsync(RegisterRequest request)
    {
        // 1. Query via Repository
        if (await _userRepository.ExistsByEmailAsync(request.Email))
            return (false, null, "Email is already registered.");

        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
        
        int assignedRoleId = 2; // Default to Customer for PoLP
        var user = new User(request.Email, hashedPassword, assignedRoleId);
        
        if (assignedRoleId == 2)
        {
            user.SetCustomerProfile(new CustomerProfile(user.Id));
        }
        else if (assignedRoleId == 1)
        {
            user.SetAdminProfile(new AdminProfile(user.Id, "General IT"));
        }
        
        // 2. Track entity in memory via Repository
        _userRepository.Add(user);
        
        // 3. Commit atomic transaction via Unit of Work
        await _unitOfWork.SaveChangesAsync();

        // 4. Encapsulated Explicit Loading
        await _userRepository.LoadRoleAsync(user);

        // 5. Publish Event
        await _publisher.Publish(new Shared.Events.UserRegisteredIntegrationEvent(user.Id, user.Email));

        // 6. Generate JWT
        var token = GenerateJwtToken(user);
        
        return (true, new AuthResponse(user.Id, user.Email, token), null);
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> LoginAsync(LoginRequest request)
    {
        // 1. Fetch fully hydrated Aggregate Root via Repository
        var user = await _userRepository.GetByEmailAsync(request.Email);
        
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return (false, null, "Invalid email or password.");

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