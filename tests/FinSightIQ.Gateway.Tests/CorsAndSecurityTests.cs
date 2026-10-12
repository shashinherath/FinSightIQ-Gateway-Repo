using System.Net;
using FinSightIQ.Gateway.Tests.Helpers;

namespace FinSightIQ.Gateway.Tests;

public class CorsAndSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CorsAndSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SecurityHeaders_PresentOnEveryResponse()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions));
        Assert.Equal("nosniff", contentTypeOptions.First());

        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var frameOptions));
        Assert.Equal("DENY", frameOptions.First());

        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var referrerPolicy));
        Assert.Equal("strict-origin-when-cross-origin", referrerPolicy.First());
    }

    [Fact]
    public async Task CorsPreflight_PermittedOrigin_ReturnsAccessControlHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin));
        Assert.Equal("http://localhost:5173", allowOrigin.First());

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredentials));
        Assert.Equal("true", allowCredentials.First());
    }
}
