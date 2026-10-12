using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace FinSightIQ.Gateway.Middleware;

/// <summary>
/// Intercepts unhandled gateway exceptions, authorization rejections (403), and
/// downstream YARP proxy communication failures, converting them to RFC 7807 Problem Details.
/// </summary>
public sealed class GatewayProblemDetailsMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayProblemDetailsMiddleware> _logger;

    public GatewayProblemDetailsMiddleware(RequestDelegate next, ILogger<GatewayProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);

            // Handle 403 Forbidden responses (from authorization policies)
            if (context.Response.StatusCode == StatusCodes.Status403Forbidden &&
                !context.Response.HasStarted)
            {
                await WriteForbiddenProblemDetailsAsync(context);
                return;
            }

            // Inspect YARP forwarder errors (downstream connection failures / timeouts)
            var errorFeature = context.GetForwarderErrorFeature();
            if (errorFeature != null && errorFeature.Error != ForwarderError.None && !context.Response.HasStarted)
            {
                await HandleForwarderErrorAsync(context, errorFeature);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing request to {Path}", context.Request.Path);

            if (!context.Response.HasStarted)
            {
                await WriteInternalErrorProblemDetailsAsync(context);
            }
        }
    }

    private static async Task WriteForbiddenProblemDetailsAsync(HttpContext context)
    {
        context.Response.ContentType = "application/problem+json; charset=utf-8";

        var problem = new
        {
            type = "https://finsightiq.io/errors/forbidden",
            title = "Access forbidden",
            status = StatusCodes.Status403Forbidden,
            code = "FORBIDDEN",
            detail = "You do not have the required role permissions to perform this operation.",
            instance = context.Request.Path.Value,
            traceId = context.TraceIdentifier,
            errors = (object?)null
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private async Task HandleForwarderErrorAsync(HttpContext context, IForwarderErrorFeature errorFeature)
    {
        _logger.LogWarning(
            errorFeature.Exception,
            "YARP forwarder error {Error} routing request {Path} with CorrelationId={CorrelationId}",
            errorFeature.Error,
            context.Request.Path,
            context.TraceIdentifier);

        var path = context.Request.Path.Value ?? string.Empty;
        var isPrediction = path.Contains("/predictions", StringComparison.OrdinalIgnoreCase);

        var code = isPrediction ? "PREDICTION_UNAVAILABLE" : "UPSTREAM_UNAVAILABLE";
        var type = isPrediction
            ? "https://finsightiq.io/errors/prediction-unavailable"
            : "https://finsightiq.io/errors/upstream-unavailable";
        var title = isPrediction ? "Prediction service unavailable" : "Upstream service unavailable";
        var detail = isPrediction
            ? "Prediction service is currently unreachable. Please honour the Retry-After interval."
            : "The upstream service is currently unreachable. Please retry with back-off.";

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "30";
        context.Response.ContentType = "application/problem+json; charset=utf-8";

        var problem = new
        {
            type,
            title,
            status = StatusCodes.Status503ServiceUnavailable,
            code,
            detail,
            instance = path,
            traceId = context.TraceIdentifier,
            errors = (object?)null
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private static async Task WriteInternalErrorProblemDetailsAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json; charset=utf-8";

        var problem = new
        {
            type = "https://finsightiq.io/errors/internal-error",
            title = "Internal server error",
            status = StatusCodes.Status500InternalServerError,
            code = "INTERNAL_ERROR",
            detail = "An unhandled server error occurred. Please contact support quoting the traceId.",
            instance = context.Request.Path.Value,
            traceId = context.TraceIdentifier,
            errors = (object?)null
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
