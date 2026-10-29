using Microsoft.AspNetCore.Mvc;
using Modules.Users.Contracts;
using Modules.Users.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using Shared.Controllers;
namespace Modules.Users.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : BaseController
{
    private readonly IAuthService _authService;

    // We depend entirely on the abstraction (IAuthService), not the concrete class.
    public UsersController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth-limit")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var (success, data, error) = await _authService.RegisterAsync(request);
        
        if (!success)
            return Conflict(new { Message = error }); // 409 Conflict for duplicate emails

        // 201 Created is the REST standard for successful creation
        return CreatedAtAction(nameof(Login), new { email = data?.Email }, data);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-limit")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (success, data, error) = await _authService.LoginAsync(request);
        
        if (!success)
            return Unauthorized(new { Message = error }); // 401 Unauthorized for bad credentials

        return Ok(data); // 200 OK
    }
}