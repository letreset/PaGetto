using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaGetto.Web;

/// <summary>
/// Captures <see cref="OperationCanceledException" /> and converts to HTTP 409 response.
/// </summary>
public partial class OperationCancelledMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OperationCancelledMiddleware> _logger;

    public OperationCancelledMiddleware(RequestDelegate next, ILogger<OperationCancelledMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception e) when (ShouldHandleException(context, e))
        {
            try
            {
                LogRequestCancelled();

                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                return;
            }
            catch (Exception)
            {
                // If there's an exception, rethrow the original exception.
            }

            throw;
        }

        bool ShouldHandleException(HttpContext ctx, Exception e)
        {
            if (ctx.Response.HasStarted) return false;

            if (e is OperationCanceledException) return true;
            if (e.InnerException is OperationCanceledException) return true;

            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request cancelled")]
    private partial void LogRequestCancelled();
}
