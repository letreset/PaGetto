using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace PaGetto.Web;

/// <summary>
/// Marks a successful response as <c>Cache-Control: private, no-cache</c> with an ETag computed
/// from its body, and answers <c>304 Not Modified</c> when the request's If-None-Match matches.
/// </summary>
/// <remarks>
/// Responses stay private because every endpoint is authenticated, and must be revalidated because
/// a feed that allows package overwrites can change a version's content. The ETag is weak because
/// response compression changes the bytes on the wire.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ContentETagAttribute : Attribute, IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var response = context.HttpContext.Response;
        var originalBody = response.Body;

        using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            await next();
        }
        finally
        {
            response.Body = originalBody;
        }

        if (response.StatusCode == StatusCodes.Status200OK)
        {
            var hash = SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(hash)}\"", isWeak: true);

            response.Headers.CacheControl = "private, no-cache";
            response.Headers.ETag = etag.ToString();

            var ifNoneMatch = context.HttpContext.Request.GetTypedHeaders().IfNoneMatch;
            if (ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false)))
            {
                response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.HttpContext.RequestAborted);
    }
}
