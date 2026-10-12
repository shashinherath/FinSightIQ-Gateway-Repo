using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class AuthenticationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidToken_AllowsAccessToProtectedEndpoint()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Viewer");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingToken_OnProtectedRoute_Returns401Unauthenticated()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("UNAUTHENTICATED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task InvalidSignature_Returns401Unauthenticated()
    {
        // Arrange: Sign token with a different RSA keypair
        using var untrustedRsa = RSA.Create(2048);
        var token = _factory.JwtGenerator.GenerateToken(role: "Viewer", alternateRsa: untrustedRsa);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("UNAUTHENTICATED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExpiredToken_Returns401TokenExpired()
    {
        // Arrange: Generate token that expired 5 minutes ago
        var token = _factory.JwtGenerator.GenerateToken(
            role: "Viewer",
            lifetime: TimeSpan.FromMinutes(-5));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("TOKEN_EXPIRED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WrongIssuer_Returns401Unauthenticated()
    {
        // Arrange: Token issued by untrusted authority
        var token = _factory.JwtGenerator.GenerateToken(
            role: "Viewer",
            issuer: "untrusted-issuer");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudience_Returns401Unauthenticated()
    {
        // Arrange: Token intended for another audience
        var token = _factory.JwtGenerator.GenerateToken(
            role: "Viewer",
            audience: "other-service");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
