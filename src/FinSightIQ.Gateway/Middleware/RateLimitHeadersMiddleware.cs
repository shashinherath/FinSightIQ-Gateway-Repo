using System.Security.Claims;
using FinSightIQ.Gateway.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FinSightIQ.Gateway.Middleware;

/// <summary>
/// Ensures standard X-RateLimit-* headers are emitted on responses according
/// to caller authentication tier and endpoint requirements.
/// </summary>
public sealed class RateLimitHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<GatewayOptions> _options;

    public RateLimitHeadersMiddleware(RequestDelegate next, IOptions<GatewayOptions> options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Only populate if not already populated (e.g. by prediction quota middleware)
            if (!headers.ContainsKey("X-RateLimit-Limit"))
            {
                var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
                var rateLimitConfig = _options.Value.RateLimiting;
                var limit = isAuthenticated
                    ? rateLimitConfig.AuthenticatedPermitLimit
                    : rateLimitConfig.AnonymousPermitLimit;

                var reset = DateTimeOffset.UtcNow.AddSeconds(rateLimitConfig.SlidingWindowSeconds).ToUnixTimeSeconds();

                headers["X-RateLimit-Limit"] = limit.ToString();
                if (!headers.ContainsKey("X-RateLimit-Remaining"))
                {
                    headers["X-RateLimit-Remaining"] = (limit - 1).ToString();
                }
                headers["X-RateLimit-Reset"] = reset.ToString();
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
