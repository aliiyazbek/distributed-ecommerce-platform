using Modules.Users.Contracts;

namespace Modules.Users.Interfaces;

public interface IAuthService
{
    // We use a C# Tuple to return success state and data without relying on expensive Exception throwing.
    Task<(bool Success, AuthResponse? Data, string? Error)> RegisterAsync(RegisterRequest request);
    Task<(bool Success, AuthResponse? Data, string? Error)> LoginAsync(LoginRequest request);
}