using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace FinSightIQ.Gateway.Tests.Helpers;

public sealed class TestJwtGenerator
{
    public RSA SigningRsa { get; }
    public string PublicKeyPem { get; }

    public TestJwtGenerator()
    {
        SigningRsa = RSA.Create(2048);
        PublicKeyPem = SigningRsa.ExportSubjectPublicKeyInfoPem();
    }

    public string GenerateToken(
        string userId = "test-user-123",
        string role = "Analyst",
        string issuer = "finsightiq",
        string audience = "finsightiq",
        TimeSpan? lifetime = null,
        RSA? alternateRsa = null)
    {
        var rsa = alternateRsa ?? SigningRsa;
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "finsightiq-key" };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        var expiry = now.Add(lifetime ?? TimeSpan.FromMinutes(15));
        var notBefore = expiry < now ? expiry.AddMinutes(-10) : now.AddMinutes(-5);

        var claims = new List<Claim>
        {
            new("sub", userId),
            new("role", role),
            new("email", $"{userId}@example.com")
        };

        var handler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            Expires = expiry,
            SigningCredentials = credentials
        };

        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }
}
