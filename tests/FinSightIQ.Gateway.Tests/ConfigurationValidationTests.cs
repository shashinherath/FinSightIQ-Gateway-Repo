using FinSightIQ.Gateway.Configuration;
using FinSightIQ.Gateway.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FinSightIQ.Gateway.Tests;

public class ConfigurationValidationTests
{
    [Fact]
    public void Production_MissingPublicKey_ThrowsInvalidOperationException()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Gateway:Jwt:PublicKeyPem"] = ""
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var mockEnv = new MockHostEnvironment { EnvironmentName = Environments.Production };
        var configureOptions = new ConfigureJwtBearerOptions(config, mockEnv);
        var jwtBearerOptions = new JwtBearerOptions();

        Assert.Throws<InvalidOperationException>(() =>
            configureOptions.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtBearerOptions));
    }

    [Fact]
    public void Development_MissingPublicKey_GeneratesDevKeyWithoutThrowing()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Gateway:Jwt:PublicKeyPem"] = ""
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var mockEnv = new MockHostEnvironment { EnvironmentName = Environments.Development };
        var configureOptions = new ConfigureJwtBearerOptions(config, mockEnv);
        var jwtBearerOptions = new JwtBearerOptions();

        configureOptions.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtBearerOptions);

        Assert.NotNull(jwtBearerOptions.TokenValidationParameters.IssuerSigningKey);
    }

    private sealed class MockHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "FinSightIQ.Gateway";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
