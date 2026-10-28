using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Modules.Users.Contracts;
using Modules.Users.Interfaces;

namespace Modules.Users.Decorators;

public class LoggingAuthServiceDecorator : IAuthService
{
    private readonly IAuthService _innerService;
    private readonly ILogger<LoggingAuthServiceDecorator> _logger;

    public LoggingAuthServiceDecorator(
        IAuthService innerService, 
        ILogger<LoggingAuthServiceDecorator> logger)
    {
        _innerService = innerService;
        _logger = logger;
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> RegisterAsync(RegisterRequest request)
    {
        _logger.LogInformation("Attempting to register user: {Email}", request.Email);
        var stopwatch = Stopwatch.StartNew();

        // Delegate the actual business logic to the inner service
        var result = await _innerService.RegisterAsync(request);

        stopwatch.Stop();
        
        if (result.Success)
            _logger.LogInformation("Successfully registered user: {Email} in {ElapsedMilliseconds}ms", request.Email, stopwatch.ElapsedMilliseconds);
        else
            _logger.LogWarning("Failed to register user: {Email}. Reason: {Error}", request.Email, result.Error);

        return result;
    }

    public async Task<(bool Success, AuthResponse? Data, string? Error)> LoginAsync(LoginRequest request)
    {
        _logger.LogInformation("Login attempt for user: {Email}", request.Email);
        var stopwatch = Stopwatch.StartNew();

        var result = await _innerService.LoginAsync(request);

        stopwatch.Stop();

        if (result.Success)
            _logger.LogInformation("Successful login for user: {Email} in {ElapsedMilliseconds}ms", request.Email, stopwatch.ElapsedMilliseconds);
        else
            _logger.LogWarning("Failed login attempt for user: {Email}. Reason: {Error}", request.Email, result.Error);

        return result;
    }
}