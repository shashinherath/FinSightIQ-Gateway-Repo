using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class IdentityPropagationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IdentityPropagationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SpoofedHeaders_AreStrippedAndReplacedWithValidatedClaims()
    {
        // Arrange: Client attempts to spoof an Admin identity
        var verifiedUserId = "analyst-user-42";
        var verifiedRole = "Analyst";
        var token = _factory.JwtGenerator.GenerateToken(userId: verifiedUserId, role: verifiedRole);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/market/AAPL/history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Spoofed untrusted headers from client
        request.Headers.Add("X-User-Id", "spoofed-admin-999");
        request.Headers.Add("X-User-Role", "Administrator");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(_factory.DownstreamHandler.Requests.TryDequeue(out var recordedRequest));

        // Downstream MUST receive the verified claims, NOT the spoofed headers
        Assert.True(recordedRequest.Headers.TryGetValue("X-User-Id", out var userIdValues));
        Assert.Equal(verifiedUserId, userIdValues.First());

        Assert.True(recordedRequest.Headers.TryGetValue("X-User-Role", out var roleValues));
        Assert.Equal(verifiedRole, roleValues.First());
    }

    [Fact]
    public async Task UnauthenticatedRequest_HasSpoofedIdentityHeadersStripped()
    {
        // Arrange: Anonymous call to /api/v1/auth/login with spoofed headers
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-User-Id", "spoofed-admin");
        request.Headers.Add("X-User-Role", "Administrator");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(_factory.DownstreamHandler.Requests.TryDequeue(out var recordedRequest));

        // Downstream MUST NOT receive any identity headers for an unauthenticated request
        Assert.False(recordedRequest.Headers.ContainsKey("X-User-Id"));
        Assert.False(recordedRequest.Headers.ContainsKey("X-User-Role"));
    }
}
