using FinSightIQ.Gateway.Configuration;
using FinSightIQ.Gateway.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Forwarder;

namespace FinSightIQ.Gateway.Tests.Helpers;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestJwtGenerator JwtGenerator { get; } = new();
    public TestDownstreamHandler DownstreamHandler { get; } = new();
    public InMemoryPredictionQuotaStore QuotaStore { get; } = new();

    public Action<IServiceCollection>? AdditionalServicesConfiguration { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["Gateway:Jwt:Issuer"] = "finsightiq",
                ["Gateway:Jwt:Audience"] = "finsightiq",
                ["Gateway:Jwt:PublicKeyPem"] = JwtGenerator.PublicKeyPem,
                ["Jwt:PublicKeyPem"] = JwtGenerator.PublicKeyPem,
                ["Gateway:RateLimiting:AnonymousPermitLimit"] = "60",
                ["Gateway:RateLimiting:AuthenticatedPermitLimit"] = "600",
                ["Gateway:RateLimiting:PredictionDailyQuota"] = "50",
                ["Gateway:Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Gateway:DownstreamServices:userService:BaseUrl"] = "http://localhost:5101",
                ["Gateway:DownstreamServices:marketDataService:BaseUrl"] = "http://localhost:5102",
                ["Gateway:DownstreamServices:predictionService:BaseUrl"] = "http://localhost:5103",
                ["Gateway:DownstreamServices:portfolioService:BaseUrl"] = "http://localhost:5104",
                ["Gateway:DownstreamServices:sentimentService:BaseUrl"] = "http://localhost:5105"
            };

            config.AddInMemoryCollection(inMemorySettings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace YARP HTTP Forwarder client factory with our TestDownstreamHandler
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(new TestForwarderHttpClientFactory(DownstreamHandler));

            // Replace PredictionQuotaStore with our test instance
            services.RemoveAll<IPredictionQuotaStore>();
            services.AddSingleton<IPredictionQuotaStore>(QuotaStore);

            // Configure HealthCheckClient to use DownstreamHandler
            services.AddHttpClient("HealthCheckClient")
                .ConfigurePrimaryHttpMessageHandler(() => DownstreamHandler);

            AdditionalServicesConfiguration?.Invoke(services);
        });
    }
}
