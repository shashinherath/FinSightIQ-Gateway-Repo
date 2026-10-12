using System.Diagnostics;
using System.Text.Json;
using FinSightIQ.Gateway.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FinSightIQ.Gateway.HealthChecks;

/// <summary>
/// Health check endpoints and downstream aggregate dependency checking.
/// </summary>
public static class GatewayHealthCheckExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapGatewayHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // 1. Liveness endpoint: indicates process is alive
        endpoints.MapGet("/health/live", async context =>
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("{\"status\":\"Healthy\"}");
        }).AllowAnonymous();

        // 2. Readiness endpoint: indicates gateway is ready to accept traffic
        endpoints.MapGet("/health/ready", async context =>
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("{\"status\":\"Healthy\"}");
        }).AllowAnonymous();

        // 3. Aggregated health endpoint: checks downstream dependencies with timeouts
        endpoints.MapGet("/health", async context =>
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("FinSightIQ.Gateway.HealthChecks");
            var options = context.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value;
            var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
            var redis = context.RequestServices.GetService<IConnectionMultiplexer>();

            var dependencyResults = new Dictionary<string, object>();
            var allHealthy = true;

            // Check configured downstream services
            foreach (var (key, service) in options.DownstreamServices)
            {
                var sw = Stopwatch.StartNew();
                var isServiceHealthy = false;
                string? statusMessage = null;

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(service.TimeoutSeconds));
                    var client = httpClientFactory.CreateClient("HealthCheckClient");

                    var healthUri = new Uri(new Uri(service.BaseUrl), service.HealthEndpoint);
                    var response = await client.GetAsync(healthUri, cts.Token);

                    sw.Stop();
                    isServiceHealthy = response.IsSuccessStatusCode;
                    statusMessage = isServiceHealthy ? "Healthy" : $"Unhealthy: Status {response.StatusCode}";
                }
                catch (OperationCanceledException)
                {
                    sw.Stop();
                    statusMessage = "Unhealthy: Timed out";
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    logger.LogWarning(ex, "Health check failed for downstream dependency {ServiceName}", service.Name);
                    statusMessage = "Unhealthy: Connection error";
                }

                if (!isServiceHealthy && service.IsCritical)
                {
                    allHealthy = false;
                }

                dependencyResults[key] = new
                {
                    status = isServiceHealthy ? "Healthy" : "Unhealthy",
                    isCritical = service.IsCritical,
                    responseTimeMs = sw.ElapsedMilliseconds,
                    detail = statusMessage
                };
            }

            // Check Redis if available
            if (redis != null)
            {
                var sw = Stopwatch.StartNew();
                var isRedisHealthy = false;
                try
                {
                    var ping = await redis.GetDatabase().PingAsync();
                    sw.Stop();
                    isRedisHealthy = ping.TotalMilliseconds >= 0;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    logger.LogWarning(ex, "Redis ping health check failed");
                }

                dependencyResults["redis"] = new
                {
                    status = isRedisHealthy ? "Healthy" : "Unhealthy",
                    isCritical = false,
                    responseTimeMs = sw.ElapsedMilliseconds
                };
            }

            var overallStatus = allHealthy ? "Healthy" : "Unhealthy";
            context.Response.StatusCode = allHealthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json; charset=utf-8";

            var responsePayload = new
            {
                status = overallStatus,
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                dependencies = dependencyResults
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(responsePayload, JsonOptions));
        }).AllowAnonymous();

        return endpoints;
    }
}
