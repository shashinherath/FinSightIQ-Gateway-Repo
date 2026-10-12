using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class RoutingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RoutingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/logout")]
    public async Task PublicAuthRoutes_ForwardToUserService_WithoutAuthentication(string endpoint)
    {
        // Arrange
        var requestBody = "{\"email\":\"test@example.com\",\"password\":\"SecurePass123!\"}";
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync(endpoint, content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(_factory.DownstreamHandler.Requests.TryDequeue(out var recordedRequest));
        Assert.Equal("POST", recordedRequest.Method);
        Assert.Equal(endpoint, recordedRequest.RequestUri.AbsolutePath);
        Assert.Equal(requestBody, recordedRequest.Body);
    }

    [Fact]
    public async Task ProtectedRoute_ForwardsMethodQueryAndBody_WhenAuthenticated()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Analyst");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var query = "?from=2026-01-01&to=2026-04-01&limit=25";
        var endpoint = $"/api/v1/market/AAPL/history{query}";

        // Act
        var response = await _client.GetAsync(endpoint);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(_factory.DownstreamHandler.Requests.TryDequeue(out var recordedRequest));
        Assert.Equal("GET", recordedRequest.Method);
        Assert.Equal("/api/v1/market/AAPL/history", recordedRequest.RequestUri.AbsolutePath);
        Assert.Equal(query, recordedRequest.RequestUri.Query);
    }

    [Fact]
    public async Task PostRoute_ForwardsPayloadCorrectly()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Analyst");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = "{\"symbol\":\"AAPL\",\"days\":7,\"model\":\"ensemble\"}";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/predictions", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(_factory.DownstreamHandler.Requests.TryDequeue(out var recordedRequest));
        Assert.Equal("POST", recordedRequest.Method);
        Assert.Equal("/api/v1/predictions", recordedRequest.RequestUri.AbsolutePath);
        Assert.Equal(payload, recordedRequest.Body);
    }
}
