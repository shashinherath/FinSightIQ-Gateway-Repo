using FinSightIQ.BuildingBlocks.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinSightIQ.Gateway.Middleware;

/// <summary>
/// Ensures every request has a validated or generated X-Correlation-Id header,
/// attaches it to the response and trace context, and creates a structured logging scope.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        CorrelationId correlationId;

        if (context.Request.Headers.TryGetValue(CorrelationId.HeaderName, out var incoming) &&
            CorrelationId.TryParse(incoming.ToString(), out var parsedId))
        {
            correlationId = parsedId;
        }
        else
        {
            correlationId = CorrelationId.NewId();
            // Replace any invalid incoming header on the request with the valid generated one
            context.Request.Headers[CorrelationId.HeaderName] = correlationId.Value;
        }

        // Always set on the response header so caller receives it
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId.Value;
            return Task.CompletedTask;
        });

        // Set on TraceIdentifier for ASP.NET Core internal diagnostics
        context.TraceIdentifier = correlationId.Value;

        // Structured logging scope for every downstream step
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId.Value
        }))
        {
            await _next(context);
        }
    }
}
