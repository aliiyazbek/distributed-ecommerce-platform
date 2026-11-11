using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Shared.Http;

/// <summary>
/// HTTP caching with ETag (Requirement 3).
/// For a successful GET it hashes the response body into a strong ETag. If the client sends
/// If-None-Match with the same value, the body is dropped and 304 Not Modified is returned.
/// Cache-Control: private, no-cache means "you may store it, but revalidate every time",
/// so an edited product is seen immediately (the revalidation returns a new ETag + 200).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ETagAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => true;
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => new ETagFilter();
}

public sealed class ETagFilter : IAsyncResultFilter
{
    private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web);

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var response = context.HttpContext.Response;

        if (!HttpMethods.IsGet(request.Method) ||
            context.Result is not ObjectResult { StatusCode: null or 200, Value: not null } ok)
        {
            await next();
            return;
        }

        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(ok.Value, ok.Value.GetType(), HashJson));
        var etag = new EntityTagHeaderValue($"\"{Convert.ToBase64String(hash, 0, 16)}\"");

        response.Headers.ETag = etag.ToString();
        response.Headers.CacheControl = "private, no-cache";
        response.Headers.Vary = "Authorization";

        if (request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var header) &&
            EntityTagHeaderValue.TryParseList(header, out var clientTags) &&
            clientTags.Any(t => t.Equals(EntityTagHeaderValue.Any) || t.Compare(etag, useStrongComparison: false)))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        await next();
    }
}
