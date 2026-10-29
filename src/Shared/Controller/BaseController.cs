using Microsoft.AspNetCore.Mvc;

namespace Shared.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    // A single unified method to format all business errors across all modules
    protected IActionResult ProblemResponse(int statusCode, string title, string? detail)
    {
        return StatusCode(statusCode, new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = HttpContext.Request.Path
        });
    }
}