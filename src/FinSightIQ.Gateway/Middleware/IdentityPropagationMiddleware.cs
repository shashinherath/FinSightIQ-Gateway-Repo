using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinSightIQ.Gateway.Middleware;

/// <summary>
/// Strips untrusted client-supplied identity headers and injects verified identity
/// headers (X-User-Id and X-User-Role) from the validated JWT claims.
/// </summary>
public sealed class IdentityPropagationMiddleware
{
    public const string UserIdHeader = "X-User-Id";
    public const string UserRoleHeader = "X-User-Role";

    private readonly RequestDelegate _next;
    private readonly ILogger<IdentityPropagationMiddleware> _logger;

    public IdentityPropagationMiddleware(RequestDelegate next, ILogger<IdentityPropagationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Unconditionally strip any spoofed or client-supplied identity headers
        context.Request.Headers.Remove(UserIdHeader);
        context.Request.Headers.Remove(UserRoleHeader);

        // 2. If authenticated, extract verified claims and propagate trusted headers
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = GetClaimValue(context.User, "sub", ClaimTypes.NameIdentifier, "userId", "id");
            var role = GetClaimValue(context.User, "role", ClaimTypes.Role);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                context.Request.Headers[UserIdHeader] = userId;
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                context.Request.Headers[UserRoleHeader] = role;
            }

            _logger.LogDebug(
                "Propagating trusted identity to downstream: UserId={UserId}, Role={Role}",
                userId ?? "unknown",
                role ?? "unknown");
        }

        await _next(context);
    }

    private static string? GetClaimValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
