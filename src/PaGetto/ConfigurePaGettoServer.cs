using System;
using System.Globalization;
using System.Threading.RateLimiting;
using PaGetto.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PaGetto;

public class ConfigurePaGettoServer
    : IConfigureOptions<CorsOptions>
    , IConfigureOptions<FormOptions>
    , IConfigureOptions<ForwardedHeadersOptions>
    , IConfigureOptions<IISServerOptions>
    , IConfigureOptions<RateLimiterOptions>
{
    public const string CorsPolicy = "AllowAll";
    private readonly PaGettoOptions _paGettoOptions;

    public ConfigurePaGettoServer(IOptions<PaGettoOptions> paGettoOptions)
    {
        _paGettoOptions = paGettoOptions.Value;
    }


    public void Configure(CorsOptions options)
    {
        var cors = _paGettoOptions.Cors ?? new CorsPolicyOptions();

        options.AddPolicy(
            CorsPolicy,
            builder =>
            {
                // No configured origins keeps the historical allow-all behavior.
                if (cors.AllowedOrigins is not { Length: > 0 })
                {
                    builder.AllowAnyOrigin();
                }
                else
                {
                    builder.WithOrigins(cors.AllowedOrigins);
                    if (cors.AllowCredentials)
                    {
                        builder.AllowCredentials();
                    }
                }

                builder.AllowAnyMethod().AllowAnyHeader();
            });
    }

    public void Configure(FormOptions options)
    {
        // Allow packages up to the configured size (8 GiB by default)
        options.MultipartBodyLengthLimit = (long)_paGettoOptions.EffectiveMaxPackageSizeMiB * 1024 * 1024;
    }

    public void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

        // Do not restrict to local network/proxy
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }

    public void Configure(IISServerOptions options)
    {
        options.MaxRequestBodySize = (long)_paGettoOptions.EffectiveMaxPackageSizeMiB * 1024 * 1024;
    }

    public void Configure(RateLimiterOptions options)
    {
        var rateLimit = _paGettoOptions.RequestRateLimit ?? new RequestRateLimitOptions();
        var healthCheckPath = _paGettoOptions.HealthCheck?.Path;

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            return default;
        };

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // Probes must never be throttled. /livez runs before the limiter, but the health
            // check is mapped at the end of the pipeline.
            if (!string.IsNullOrEmpty(healthCheckPath) && context.Request.Path.Equals(healthCheckPath, StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitPartition.GetNoLimiter(string.Empty);
            }

            var userName = context.User.Identity is { IsAuthenticated: true } identity ? identity.Name : null;
            var partitionKey = userName != null
                ? $"user:{userName}"
                : $"ip:{context.Connection.RemoteIpAddress}";

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimit.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimit.WindowSeconds),
                QueueLimit = rateLimit.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
        });
    }
}
