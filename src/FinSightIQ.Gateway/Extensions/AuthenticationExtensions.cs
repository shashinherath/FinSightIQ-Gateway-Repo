using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FinSightIQ.Gateway.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinSightIQ.Gateway.Extensions;

/// <summary>
/// Configures RS256 JWT access-token authentication and role authorization policies.
/// </summary>
public static class AuthenticationExtensions
{
    public const string ViewerPolicy = "ViewerPolicy";
    public const string AnalystPolicy = "AnalystPolicy";
    public const string AdminPolicy = "AdminPolicy";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.UseSecurityTokenValidators = true;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = environment.IsProduction();
            options.SaveToken = false;

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Support SignalR WebSocket query string token passing
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/market"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();

                    var httpContext = context.HttpContext;
                    httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    httpContext.Response.ContentType = "application/problem+json; charset=utf-8";

                    var isExpired = context.AuthenticateFailure is SecurityTokenExpiredException
                        || (context.ErrorDescription != null && context.ErrorDescription.Contains("expired", StringComparison.OrdinalIgnoreCase))
                        || (context.AuthenticateFailure?.Message != null && context.AuthenticateFailure.Message.Contains("expired", StringComparison.OrdinalIgnoreCase));

                    var code = isExpired ? "TOKEN_EXPIRED" : "UNAUTHENTICATED";
                    var title = isExpired ? "Access token expired" : "Authentication failed";
                    var detail = isExpired
                        ? "Access token past expiry. Please use your refresh token to acquire a new access token."
                        : (context.ErrorDescription ?? "Missing, invalid or unverified access token.");

                    var problem = new
                    {
                        type = isExpired ? "https://finsightiq.io/errors/token-expired" : "https://finsightiq.io/errors/unauthenticated",
                        title,
                        status = StatusCodes.Status401Unauthorized,
                        code,
                        detail,
                        instance = httpContext.Request.Path.Value,
                        traceId = httpContext.TraceIdentifier,
                        errors = (object?)null
                    };

                    await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
                }
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ViewerPolicy, policy =>
                policy.RequireAssertion(ctx =>
                    HasAnyRole(ctx.User, "Viewer", "Analyst", "Administrator")));

            options.AddPolicy(AnalystPolicy, policy =>
                policy.RequireAssertion(ctx =>
                    HasAnyRole(ctx.User, "Analyst", "Administrator")));

            options.AddPolicy(AdminPolicy, policy =>
                policy.RequireAssertion(ctx =>
                    HasAnyRole(ctx.User, "Administrator")));
        });

        return services;
    }

    private static bool HasAnyRole(ClaimsPrincipal principal, params string[] allowedRoles)
    {
        var role = principal.FindFirst("role")?.Value
            ?? principal.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        return allowedRoles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase));
    }

    public static RSA LoadRsaPublicKey(string pemOrPath)
    {
        var rsa = RSA.Create();
        string pemContent;

        if (File.Exists(pemOrPath))
        {
            pemContent = File.ReadAllText(pemOrPath);
        }
        else
        {
            pemContent = pemOrPath;
        }

        rsa.ImportFromPem(pemContent);
        return rsa;
    }
}

public sealed class ConfigureJwtBearerOptions : IPostConfigureOptions<JwtBearerOptions>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public ConfigureJwtBearerOptions(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        var jwtSection = _configuration.GetSection("Gateway:Jwt").Exists()
            ? _configuration.GetSection("Gateway:Jwt")
            : _configuration.GetSection("Jwt");
        var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

        jwtOptions.Issuer = _configuration["Jwt:Issuer"]
            ?? _configuration["Gateway:Jwt:Issuer"]
            ?? jwtOptions.Issuer;
        jwtOptions.Audience = _configuration["Jwt:Audience"]
            ?? _configuration["Gateway:Jwt:Audience"]
            ?? jwtOptions.Audience;
        jwtOptions.PublicKeyPem = _configuration["Jwt:PublicKeyPem"]
            ?? _configuration["Gateway:Jwt:PublicKeyPem"]
            ?? jwtOptions.PublicKeyPem;

        RSA rsa;
        if (!string.IsNullOrWhiteSpace(jwtOptions.PublicKeyPem))
        {
            rsa = AuthenticationExtensions.LoadRsaPublicKey(jwtOptions.PublicKeyPem);
        }
        else if (_environment.IsDevelopment())
        {
            rsa = RSA.Create(2048);
        }
        else
        {
            throw new InvalidOperationException("Jwt:PublicKeyPem must be configured in staging and production environments.");
        }

        var securityKey = new RsaSecurityKey(rsa) { KeyId = "finsightiq-key" };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            IssuerSigningKeyResolver = (_, _, _, _) => [securityKey],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(jwtOptions.ClockSkewSeconds),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RequireSignedTokens = true
        };
    }
}
