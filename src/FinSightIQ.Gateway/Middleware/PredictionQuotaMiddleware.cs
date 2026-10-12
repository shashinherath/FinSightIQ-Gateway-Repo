using System.Security.Claims;
using System.Text.Json;
using FinSightIQ.Gateway.Configuration;
using FinSightIQ.Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinSightIQ.Gateway.Middleware;

/// <summary>
/// Enforces the 50 requests per rolling 24-hour quota specifically for prediction generation
/// (POST /api/v1/predictions) per authenticated user.
/// </summary>
public sealed class PredictionQuotaMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly IPredictionQuotaStore _quotaStore;
    private readonly IOptions<GatewayOptions> _options;
    private readonly ILogger<PredictionQuotaMiddleware> _logger;

    public PredictionQuotaMiddleware(
        RequestDelegate next,
        IPredictionQuotaStore quotaStore,
        IOptions<GatewayOptions> options,
        ILogger<PredictionQuotaMiddleware> logger)
    {
        _next = next;
        _quotaStore = quotaStore;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsPredictionGenerationEndpoint(context.Request))
        {
            var userId = GetUserId(context.User);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var limit = _options.Value.RateLimiting.PredictionDailyQuota;
                var window = TimeSpan.FromHours(_options.Value.RateLimiting.PredictionWindowHours);

                var quotaResult = await _quotaStore.CheckAndConsumeQuotaAsync(userId, limit, window, context.RequestAborted);

                // Attach rate limit headers to the response
                context.Response.Headers["X-RateLimit-Limit"] = quotaResult.Limit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = quotaResult.Remaining.ToString();
                context.Response.Headers["X-RateLimit-Reset"] = quotaResult.ResetTimestampSeconds.ToString();

                if (!quotaResult.IsAllowed)
                {
                    _logger.LogWarning("User {UserId} exceeded daily prediction quota ({Limit}/24h)", userId, limit);

                    var retrySeconds = Math.Max(1, (int)quotaResult.RetryAfter.TotalSeconds);
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers["Retry-After"] = retrySeconds.ToString();
                    context.Response.ContentType = "application/problem+json; charset=utf-8";

                    var problem = new
                    {
                        type = "https://finsightiq.io/errors/quota-exceeded",
                        title = "Daily prediction quota exceeded",
                        status = StatusCodes.Status429TooManyRequests,
                        code = "QUOTA_EXCEEDED",
                        detail = $"Daily prediction quota of {limit} requests reached. Please retry after {retrySeconds} seconds.",
                        instance = context.Request.Path.Value,
                        traceId = context.TraceIdentifier,
                        errors = (object?)null
                    };

                    await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
                    return;
                }
            }
        }

        await _next(context);
    }

    private static bool IsPredictionGenerationEndpoint(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;
        return path.Equals("/api/v1/predictions", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetUserId(ClaimsPrincipal principal)
    {
        return principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("userId")?.Value
            ?? principal.FindFirst("id")?.Value;
    }
}
