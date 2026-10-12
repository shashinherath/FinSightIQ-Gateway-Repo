using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class AuthorizationRoleTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthorizationRoleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Viewer_CanAccessMarketData()
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
    public async Task Viewer_CannotAccessPortfolio_Returns403Forbidden()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Viewer");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/portfolio");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("FORBIDDEN", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Viewer_CannotCreatePrediction_Returns403Forbidden()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Viewer");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/predictions")
        {
            Content = new StringContent("{\"symbol\":\"AAPL\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Analyst_CanAccessPortfolioAndPredictions()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Analyst");

        // Act & Assert 1: Portfolio
        var portfolioReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/portfolio");
        portfolioReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var portfolioResp = await _client.SendAsync(portfolioReq);
        Assert.Equal(HttpStatusCode.OK, portfolioResp.StatusCode);

        // Act & Assert 2: Prediction generate
        var predReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/predictions")
        {
            Content = new StringContent("{\"symbol\":\"AAPL\"}", Encoding.UTF8, "application/json")
        };
        predReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var predResp = await _client.SendAsync(predReq);
        Assert.Equal(HttpStatusCode.OK, predResp.StatusCode);
    }

    [Fact]
    public async Task Analyst_CannotRetrainModels_Returns403Forbidden()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Analyst");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/predictions/retrain")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("FORBIDDEN", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Administrator_CanRetrainModels()
    {
        // Arrange
        var token = _factory.JwtGenerator.GenerateToken(role: "Administrator");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/predictions/retrain")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
