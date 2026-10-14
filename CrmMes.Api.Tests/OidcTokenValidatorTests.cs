using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using CrmMes.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CrmMes.Api.Tests;

/// <summary>Non esiste ancora un IdP reale da interrogare (Azure AD, Google Workspace...): qui si prova
/// la stessa identica logica di verifica crittografica (firma, issuer, audience, scadenza) contro un
/// IdP fittizio costruito al volo — una chiave RSA generata in memoria e un JWT firmato con quella
/// chiave, nessuna rete necessaria. Appena arriverà un IdP vero basterà configurare Issuer/Audience in
/// appsettings: il codice che verifica il token è lo stesso, provato qui.</summary>
public class OidcTokenValidatorTests
{
    private const string Issuer = "https://fake-idp.test";
    private const string Audience = "crmmes-test-audience";
    private const string ProviderKey = "fakeidp";

    private static RsaSecurityKey NewSigningKey() => new(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };

    private static string MintToken(RsaSecurityKey key, string issuer = Issuer, string audience = Audience,
        string subject = "user-123", string? email = "mario.rossi@cliente.test", string? name = "Mario Rossi",
        DateTime? expires = null)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, subject) };
        if (email is not null) claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        if (name is not null) claims.Add(new Claim("name", name));

        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: expiresAt.AddMinutes(-20),
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static IConfiguration BuildConfig(string? issuer = Issuer, string? audience = Audience, bool enabled = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:External:Providers:0:Key"] = ProviderKey,
            ["Auth:External:Providers:0:Name"] = "IdP di prova",
            ["Auth:External:Providers:0:Enabled"] = enabled.ToString(),
            ["Auth:External:Providers:0:Issuer"] = issuer,
            ["Auth:External:Providers:0:Audience"] = audience,
        }).Build();

    private static OidcTokenValidator BuildValidator(RsaSecurityKey signingKey, IConfiguration? configuration = null)
    {
        var oidcConfig = new OpenIdConnectConfiguration();
        oidcConfig.SigningKeys.Add(signingKey);
        var configManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidcConfig);

        return new OidcTokenValidator(configuration ?? BuildConfig(), _ => configManager);
    }

    [Fact]
    public async Task TokenValidoFirmatoConLaChiaveGiusta_VieneAccettato()
    {
        var key = NewSigningKey();
        var validator = BuildValidator(key);

        var result = await validator.ValidateAsync(ProviderKey, MintToken(key), CancellationToken.None);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal("user-123", result.Subject);
        Assert.Equal("mario.rossi@cliente.test", result.Email);
        Assert.Equal("Mario Rossi", result.Name);
    }

    [Fact]
    public async Task FirmaConUnaChiaveDiversaDaQuellaDelProvider_VieneRifiutato()
    {
        var keyDelProvider = NewSigningKey();
        var altraChiave = NewSigningKey(); // mai registrata nella configurazione del provider
        var validator = BuildValidator(keyDelProvider);

        var result = await validator.ValidateAsync(ProviderKey, MintToken(altraChiave), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task IssuerDiverso_VieneRifiutato()
    {
        var key = NewSigningKey();
        var validator = BuildValidator(key);

        var result = await validator.ValidateAsync(ProviderKey, MintToken(key, issuer: "https://un-altro-idp.test"), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AudienceDiversa_VieneRifiutata()
    {
        var key = NewSigningKey();
        var validator = BuildValidator(key);

        var result = await validator.ValidateAsync(ProviderKey, MintToken(key, audience: "un-altro-client-id"), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task TokenScaduto_VieneRifiutato()
    {
        var key = NewSigningKey();
        var validator = BuildValidator(key);

        var result = await validator.ValidateAsync(ProviderKey, MintToken(key, expires: DateTime.UtcNow.AddMinutes(-10)), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ProviderSenzaIssuerOAudience_NonRichiedeVerifica()
    {
        var key = NewSigningKey();
        var validator = BuildValidator(key, BuildConfig(issuer: null, audience: null));

        Assert.False(validator.Providers[ProviderKey].RequiresIdToken);

        var result = await validator.ValidateAsync(ProviderKey, MintToken(key), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("non è configurato", result.FailureReason);
    }

    [Fact]
    public async Task ProviderDisattivato_VieneRifiutato()
    {
        var key = NewSigningKey();
        var validator = BuildValidator(key, BuildConfig(enabled: false));

        var result = await validator.ValidateAsync(ProviderKey, MintToken(key), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("sconosciuto o disattivato", result.FailureReason);
    }

    [Fact]
    public async Task ProviderInesistente_VieneRifiutato()
    {
        var key = NewSigningKey();
        var validator = BuildValidator(key);

        var result = await validator.ValidateAsync("provider-che-non-esiste", MintToken(key), CancellationToken.None);

        Assert.False(result.Success);
    }
}
