using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class PredictionQuotaTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PredictionQuotaTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PredictionQuota_Enforces50RequestsPerRolling24Hours()
    {
        // Arrange
        var userId = "analyst-quota-test";
        var token = _factory.JwtGenerator.GenerateToken(userId: userId, role: "Analyst");
        var content = () => new StringContent("{\"symbol\":\"AAPL\"}", Encoding.UTF8, "application/json");

        // Act: Consume 50 requests
        for (var i = 1; i <= 50; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/predictions") { Content = content() };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            // Assert rate limit headers are present
            Assert.True(res.Headers.Contains("X-RateLimit-Limit"));
            Assert.True(res.Headers.Contains("X-RateLimit-Remaining"));
            Assert.True(res.Headers.Contains("X-RateLimit-Reset"));
        }

        // 51st request MUST be rejected with 429 QUOTA_EXCEEDED
        var exceededReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/predictions") { Content = content() };
        exceededReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var exceededRes = await _client.SendAsync(exceededReq);

        Assert.Equal(HttpStatusCode.TooManyRequests, exceededRes.StatusCode);
        Assert.NotNull(exceededRes.Headers.RetryAfter);

        var body = await exceededRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("QUOTA_EXCEEDED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, doc.RootElement.GetProperty("status").GetInt32());

        // Verify user isolation: another user can still generate predictions
        var otherUserToken = _factory.JwtGenerator.GenerateToken(userId: "another-user-99", role: "Analyst");
        var otherReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/predictions") { Content = content() };
        otherReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherUserToken);

        var otherRes = await _client.SendAsync(otherReq);
        Assert.Equal(HttpStatusCode.OK, otherRes.StatusCode);
    }
}
