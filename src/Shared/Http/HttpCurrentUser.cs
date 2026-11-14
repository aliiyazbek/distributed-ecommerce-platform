using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shared.Data;

namespace Shared.Http;

// ICurrentUser for HTTP requests: the user id from the JWT ("sub", mapped to NameIdentifier).
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? UserId
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            var id = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
            return Guid.TryParse(id, out var userId) ? userId : null;
        }
    }
}
