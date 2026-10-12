using FinSightIQ.Gateway.Configuration;
using FinSightIQ.Gateway.Extensions;
using FinSightIQ.Gateway.HealthChecks;
using FinSightIQ.Gateway.Middleware;
using FinSightIQ.Gateway.Services;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Strongly-typed configuration
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));

var gatewayConfig = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>()
    ?? new GatewayOptions();

// 2. HTTP client factory for health checks
builder.Services.AddHttpClient("HealthCheckClient");

// 3. Prediction quota store (Redis when available, in-memory fallback)
var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration["Redis__ConnectionString"];

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    try
    {
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddSingleton<IPredictionQuotaStore, RedisPredictionQuotaStore>();
    }
    catch (Exception ex)
    {
        builder.Logging.AddConsole();
        var tempLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Program");
        tempLogger.LogWarning(ex, "Failed to connect to Redis at {ConnectionString}. Using InMemoryPredictionQuotaStore.", redisConnectionString);
        builder.Services.AddSingleton<IPredictionQuotaStore, InMemoryPredictionQuotaStore>();
    }
}
else
{
    builder.Services.AddSingleton<IPredictionQuotaStore, InMemoryPredictionQuotaStore>();
}

// 4. CORS configuration
const string CorsPolicyName = "GatewayCorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        var allowedOrigins = gatewayConfig.Cors.AllowedOrigins;
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();

            if (gatewayConfig.Cors.AllowCredentials)
            {
                policy.AllowCredentials();
            }
        }
    });
});

// 5. Authentication & Authorization (RS256 JWT validation)
builder.Services.AddGatewayAuthentication(builder.Configuration, builder.Environment);

// 6. Rate Limiting
builder.Services.AddGatewayRateLimiting();

// 7. YARP Reverse Proxy
builder.Services.AddGatewayReverseProxy(builder.Configuration);

var app = builder.Build();

// 8. Middleware Pipeline Execution
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<GatewayProblemDetailsMiddleware>();

app.UseCors(CorsPolicyName);

app.UseAuthentication();
app.UseMiddleware<IdentityPropagationMiddleware>();
app.UseAuthorization();

app.UseMiddleware<RateLimitHeadersMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<PredictionQuotaMiddleware>();

// 9. Endpoints
app.MapGatewayHealthChecks();
app.MapReverseProxy();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
