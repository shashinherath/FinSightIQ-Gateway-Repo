namespace FinSightIQ.Gateway.Configuration;

/// <summary>
/// Root gateway configuration options.
/// </summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public JwtOptions Jwt { get; set; } = new();

    public CorsOptions Cors { get; set; } = new();

    public RateLimitOptions RateLimiting { get; set; } = new();

    public Dictionary<string, DownstreamServiceConfig> DownstreamServices { get; set; } = new();
}

/// <summary>
/// Options for RS256 JWT validation.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "finsightiq";

    public string Audience { get; set; } = "finsightiq";

    /// <summary>
    /// The RSA public key in PEM format (or path to a PEM file).
    /// Private keys must never be provided to the gateway.
    /// </summary>
    public string? PublicKeyPem { get; set; }

    /// <summary>
    /// Clock skew leeway for token expiry.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 60;
}

/// <summary>
/// Options for Cross-Origin Resource Sharing.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173"];

    public bool AllowCredentials { get; set; } = true;
}

/// <summary>
/// Configuration for gateway rate limiting.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Sliding-window minute limit for anonymous callers (by client IP).
    /// </summary>
    public int AnonymousPermitLimit { get; set; } = 60;

    /// <summary>
    /// Sliding-window minute limit for authenticated callers (by user ID).
    /// </summary>
    public int AuthenticatedPermitLimit { get; set; } = 600;

    /// <summary>
    /// Sliding window duration in seconds for general requests.
    /// </summary>
    public int SlidingWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Rolling 24-hour prediction limit per user.
    /// </summary>
    public int PredictionDailyQuota { get; set; } = 50;

    /// <summary>
    /// Rolling window hours for prediction quota.
    /// </summary>
    public int PredictionWindowHours { get; set; } = 24;
}

/// <summary>
/// Downstream service definition for health checks and address resolution.
/// </summary>
public sealed class DownstreamServiceConfig
{
    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string HealthEndpoint { get; set; } = "/health/live";

    public bool IsCritical { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 3;
}
