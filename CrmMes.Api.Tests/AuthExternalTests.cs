using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using CrmMes.Api.Controllers;
using Microsoft.IdentityModel.Tokens;

namespace CrmMes.Api.Tests;

public class AuthExternalTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public AuthExternalTests(AdminSeededApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExternalLogin_ProvidersAutoProvisionAndRefusal()
    {
        var providers = await _fixture.Client.GetFromJsonAsync<List<ExternalProviderResponse>>("/api/auth/external/providers");
        Assert.Contains(providers!, p => p.Key == "dev" && p.Enabled);

        await using var noProvisionFactory = new CustomWebApplicationFactory { ExternalAutoProvision = false };
        await noProvisionFactory.InitializeDatabaseAsync();
        using var noProvisionClient = noProvisionFactory.CreateClient();
        var refused = await noProvisionClient.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "dev", "unknown-subject", "nuovo@test.local", "Nuovo", "web"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var email = $"sso-{Guid.NewGuid():N}@test.local";
        var response = await _fixture.Client.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "dev", $"sub-{Guid.NewGuid():N}", email, "SSO Tester", "web"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        Assert.Equal("Operator", auth.Role);
        Assert.Equal(email, auth.Email);

        var again = await _fixture.Client.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "dev", $"other-{Guid.NewGuid():N}", email, "SSO Tester", "web"));
        again.EnsureSuccessStatusCode();
        var same = (await again.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(auth.UserId, same.UserId);
    }

    private const string VerifiedIssuer = "https://fake-idp.test";
    private const string VerifiedAudience = "crmmes-test-audience";

    private static string MintIdToken(RsaSecurityKey key, string subject, string email, string name) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: VerifiedIssuer, audience: VerifiedAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, subject), new Claim(JwtRegisteredClaimNames.Email, email), new Claim("name", name)],
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));

    /// <summary>Un provider con Issuer/Audience configurati (non ce n'è uno reale oggi: qui lo si
    /// simula con una chiave RSA e un IdP fittizio, stessa logica di verifica di un IdP vero) deve
    /// ignorare Subject/Email/Name dichiarati dal client e fidarsi solo di un id_token verificato.</summary>
    [Fact]
    public async Task ExternalLogin_ProviderVerificato_RichiedeIdTokenEIgnoraIClaimDelClient()
    {
        var signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };
        await using var factory = new CustomWebApplicationFactory
        {
            VerifiedOidcProvider = ("verified", VerifiedIssuer, VerifiedAudience, signingKey),
        };
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var providers = await client.GetFromJsonAsync<List<ExternalProviderResponse>>("/api/auth/external/providers");
        Assert.Contains(providers!, p => p.Key == "verified" && p.RequiresIdToken);
        Assert.Contains(providers!, p => p.Key == "dev" && !p.RequiresIdToken);

        var senzaToken = await client.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "verified", "soggetto-dichiarato-dal-client", "finto@attaccante.test", "Non Verificato", "web"));
        Assert.Equal(HttpStatusCode.BadRequest, senzaToken.StatusCode);

        var idToken = MintIdToken(signingKey, "soggetto-reale", "vero@cliente.test", "Utente Vero");
        var conToken = await client.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "verified", Subject: "soggetto-dichiarato-dal-client", Email: "finto@attaccante.test", Name: "Non Verificato",
            Channel: "web", IdToken: idToken));
        var body = await conToken.Content.ReadAsStringAsync();
        Assert.True(conToken.IsSuccessStatusCode, body);
        var auth = (await conToken.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal("vero@cliente.test", auth.Email); // mai "finto@attaccante.test": vincono i claim verificati

        var tokenManomesso = idToken[..^2] + "zz";
        var conTokenManomesso = await client.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "verified", null, null, null, "web", tokenManomesso));
        Assert.Equal(HttpStatusCode.Unauthorized, conTokenManomesso.StatusCode);
    }
}
