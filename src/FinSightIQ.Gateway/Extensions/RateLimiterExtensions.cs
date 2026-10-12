using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using FinSightIQ.Gateway.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinSightIQ.Gateway.Extensions;

/// <summary>
/// Configures ASP.NET Core rate limiting with partition strategies for anonymous (by IP)
/// and authenticated callers (by validated user ID).
/// </summary>
public static class RateLimiterExtensions
{
    public const string PolicyName = "GatewayRateLimitPolicy";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                var httpContext = context.HttpContext;
                var retryAfterSeconds = 60;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    retryAfterSeconds = Math.Max(1, (int)retryAfter.TotalSeconds);
                }

                httpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                httpContext.Response.ContentType = "application/problem+json; charset=utf-8";

                var problem = new
                {
                    type = "https://finsightiq.io/errors/rate-limited",
                    title = "Rate limit exceeded",
                    status = StatusCodes.Status429TooManyRequests,
                    code = "RATE_LIMITED",
                    detail = $"Rate limit exceeded. Please retry after {retryAfterSeconds} seconds.",
                    instance = httpContext.Request.Path.Value,
                    traceId = httpContext.TraceIdentifier,
                    errors = (object?)null
                };

                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions), token);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var gatewayOptions = httpContext.RequestServices
                    .GetRequiredService<IOptions<GatewayOptions>>().Value;

                var rateLimitConfig = gatewayOptions.RateLimiting;
                var isAuthenticated = httpContext.User.Identity?.IsAuthenticated == true;

                if (isAuthenticated)
                {
                    var userId = GetUserId(httpContext.User) ?? "authenticated-unknown";
                    var partitionKey = $"user:{userId}";

                    return RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey,
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = rateLimitConfig.AuthenticatedPermitLimit,
                            Window = TimeSpan.FromSeconds(rateLimitConfig.SlidingWindowSeconds),
                            SegmentsPerWindow = 6,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        });
                }
                else
                {
                    var clientIp = ResolveClientIp(httpContext);
                    var partitionKey = $"ip:{clientIp}";

                    return RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey,
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = rateLimitConfig.AnonymousPermitLimit,
                            Window = TimeSpan.FromSeconds(rateLimitConfig.SlidingWindowSeconds),
                            SegmentsPerWindow = 6,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        });
                }
            });
        });

        return services;
    }

    private static string? GetUserId(ClaimsPrincipal principal)
    {
        return principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("userId")?.Value
            ?? principal.FindFirst("id")?.Value;
    }

    private static string ResolveClientIp(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) &&
            !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstIp = forwardedFor.ToString().Split(',', StringSplitOptions.TrimEntries)[0];
            if (!string.IsNullOrWhiteSpace(firstIp))
            {
                return firstIp;
            }
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
    }
}
