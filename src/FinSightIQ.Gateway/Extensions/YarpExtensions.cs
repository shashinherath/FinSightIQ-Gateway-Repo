using System.Security.Claims;
using FinSightIQ.BuildingBlocks.Middleware;
using FinSightIQ.Gateway.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Transforms;

namespace FinSightIQ.Gateway.Extensions;

/// <summary>
/// Configures YARP reverse proxy with configuration-based routing and secure transforms.
/// </summary>
public static class YarpExtensions
{
    public static IServiceCollection AddGatewayReverseProxy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))
            .AddTransforms(builderContext =>
            {
                // Defense-in-depth: Ensure transforms remove untrusted client identity headers
                // and inject only verified claims
                builderContext.AddRequestTransform(transformContext =>
                {
                    var httpContext = transformContext.HttpContext;

                    // Strip any untrusted incoming identity headers
                    transformContext.ProxyRequest.Headers.Remove(IdentityPropagationMiddleware.UserIdHeader);
                    transformContext.ProxyRequest.Headers.Remove(IdentityPropagationMiddleware.UserRoleHeader);

                    // Forward CorrelationId
                    if (httpContext.Request.Headers.TryGetValue(CorrelationId.HeaderName, out var correlationId))
                    {
                        transformContext.ProxyRequest.Headers.Remove(CorrelationId.HeaderName);
                        transformContext.ProxyRequest.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, correlationId.ToString());
                    }

                    // Forward authenticated identity
                    if (httpContext.User.Identity?.IsAuthenticated == true)
                    {
                        var userId = httpContext.User.FindFirst("sub")?.Value
                            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? httpContext.User.FindFirst("userId")?.Value
                            ?? httpContext.User.FindFirst("id")?.Value;

                        var role = httpContext.User.FindFirst("role")?.Value
                            ?? httpContext.User.FindFirst(ClaimTypes.Role)?.Value;

                        if (!string.IsNullOrWhiteSpace(userId))
                        {
                            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                                IdentityPropagationMiddleware.UserIdHeader, userId);
                        }

                        if (!string.IsNullOrWhiteSpace(role))
                        {
                            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                                IdentityPropagationMiddleware.UserRoleHeader, role);
                        }
                    }

                    return ValueTask.CompletedTask;
                });
            });

        return services;
    }
}
