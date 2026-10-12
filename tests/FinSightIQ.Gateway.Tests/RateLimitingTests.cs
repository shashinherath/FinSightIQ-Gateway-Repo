using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinSightIQ.Gateway.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FinSightIQ.Gateway.Tests;

public class CustomRateLimitFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var lowLimits = new Dictionary<string, string?>
            {
                ["Gateway:RateLimiting:AnonymousPermitLimit"] = "3",
                ["Gateway:RateLimiting:AuthenticatedPermitLimit"] = "5",
                ["Gateway:RateLimiting:SlidingWindowSeconds"] = "60"
            };
            config.AddInMemoryCollection(lowLimits);
        });
    }
}

public class RateLimitingTests : IClassFixture<CustomRateLimitFactory>
{
    private readonly CustomRateLimitFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingTests(CustomRateLimitFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AnonymousCaller_RateLimitEnforced_Returns429RateLimited()
    {
        // Act: Make 3 successful calls
        for (var i = 0; i < 3; i++)
        {
            var res = await _client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.True(res.Headers.Contains("X-RateLimit-Limit"));
        }

        // 4th call should be rate limited
        var rejectedRes = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedRes.StatusCode);
        Assert.NotNull(rejectedRes.Headers.RetryAfter);

        var body = await rejectedRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("RATE_LIMITED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AuthenticatedCaller_RateLimitEnforced_WithUserIsolation()
    {
        var userA = "user-rate-a";
        var userB = "user-rate-b";
        var tokenA = _factory.JwtGenerator.GenerateToken(userId: userA, role: "Viewer");
        var tokenB = _factory.JwtGenerator.GenerateToken(userId: userB, role: "Viewer");

        // Act: Consume user A quota (limit = 5)
        for (var i = 0; i < 5; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // 6th call for User A should be rate limited
        var reqA6 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        reqA6.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var resA6 = await _client.SendAsync(reqA6);
        Assert.Equal(HttpStatusCode.TooManyRequests, resA6.StatusCode);

        // User B should NOT be impacted by User A's rate limit
        var reqB = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        reqB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var resB = await _client.SendAsync(reqB);
        Assert.Equal(HttpStatusCode.OK, resB.StatusCode);
    }
}
