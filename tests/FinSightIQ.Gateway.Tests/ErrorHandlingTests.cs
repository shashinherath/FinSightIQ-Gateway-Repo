using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class ErrorHandlingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ErrorHandlingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DownstreamFailure_Returns503UpstreamUnavailable_WithProblemDetails()
    {
        var token = _factory.JwtGenerator.GenerateToken(role: "Viewer");

        _factory.DownstreamHandler.SimulateFailure = true;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.NotNull(response.Headers.RetryAfter);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal("UPSTREAM_UNAVAILABLE", root.GetProperty("code").GetString());
            Assert.Equal(503, root.GetProperty("status").GetInt32());
            Assert.True(root.TryGetProperty("traceId", out _));
            Assert.True(root.TryGetProperty("type", out _));
        }
        finally
        {
            _factory.DownstreamHandler.SimulateFailure = false;
        }
    }

    [Fact]
    public async Task PredictionDownstreamFailure_Returns503PredictionUnavailable()
    {
        var token = _factory.JwtGenerator.GenerateToken(role: "Analyst");

        _factory.DownstreamHandler.SimulateFailure = true;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/predictions/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal("PREDICTION_UNAVAILABLE", root.GetProperty("code").GetString());
            Assert.Equal(503, root.GetProperty("status").GetInt32());
        }
        finally
        {
            _factory.DownstreamHandler.SimulateFailure = false;
        }
    }
}
