using System.Net;
using FinSightIQ.BuildingBlocks.Middleware;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class CorrelationIdTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CorrelationIdTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MissingCorrelationId_GeneratesNew32HexIdentifier_AndPropagates()
    {
        // Act
        var response = await _client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.TryGetValues(CorrelationId.HeaderName, out var responseHeaders));
        var correlationId = responseHeaders.First();

        Assert.Equal(32, correlationId.Length);
        Assert.True(CorrelationId.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task ValidClientCorrelationId_PreservedInResponse_AndPropagatedDownstream()
    {
        // Arrange
        var customId = "custom-test-trace-id-12345";
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationId.HeaderName, customId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.TryGetValues(CorrelationId.HeaderName, out var responseHeaders));
        Assert.Equal(customId, responseHeaders.First());
    }

    [Fact]
    public async Task InvalidClientCorrelationId_ReplacedWithValidGeneratedIdentifier()
    {
        // Arrange: Value with illegal character '@' or too long (> 64)
        var invalidId = "invalid@character#trace$";
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationId.HeaderName, invalidId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.TryGetValues(CorrelationId.HeaderName, out var responseHeaders));
        var returnedId = responseHeaders.First();

        Assert.NotEqual(invalidId, returnedId);
        Assert.Equal(32, returnedId.Length);
        Assert.True(CorrelationId.TryParse(returnedId, out _));
    }
}
