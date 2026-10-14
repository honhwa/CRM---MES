using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CrmMes.Api.Services;

public sealed record OidcProviderConfig(string Key, string Name, bool Enabled, string? Issuer, string? Audience)
{
    /// <summary>Senza Issuer e Audience non c'è niente contro cui verificare un token: il provider resta
    /// nel comportamento storico (si fida di quello che manda il client, solo per sviluppo locale/demo,
    /// mai per un IdP vero). Appena un cliente fornisce un IdP reale, basta valorizzare questi due campi
    /// in configurazione — nessun codice da cambiare — e la verifica scatta da sola.</summary>
    public bool RequiresIdToken => !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience);
}

public sealed record OidcValidationResult(bool Success, string? Subject, string? Email, string? Name, string? FailureReason)
{
    public static OidcValidationResult Fail(string reason) => new(false, null, null, null, reason);
    public static OidcValidationResult Ok(string subject, string? email, string? name) => new(true, subject, email, name, null);
}

public interface IOidcTokenValidator
{
    IReadOnlyDictionary<string, OidcProviderConfig> Providers { get; }
    Task<OidcValidationResult> ValidateAsync(string providerKey, string idToken, CancellationToken cancellationToken);
}

/// <summary>Verifica crittografica reale (firma, issuer, audience, scadenza) di un id_token OIDC, al posto
/// di fidarsi di quello che il client dichiara. Scarica e mette in cache la configurazione/chiavi di
/// firma del provider dal suo `.well-known/openid-configuration` (comportamento standard di
/// Microsoft.IdentityModel.Protocols.OpenIdConnect, già usato altrove in ASP.NET Core per l'autenticazione
/// JWT bearer basata su Authority — qui applicato all'exchange SSO invece che all'autenticazione delle
/// richieste). Il resolver dei provider è iniettabile per i test, che così possono puntare a un IdP
/// fittizio costruito al volo invece di dipendere da una rete o da un tenant reale.</summary>
public sealed class OidcTokenValidator : IOidcTokenValidator
{
    private readonly IReadOnlyDictionary<string, OidcProviderConfig> _providers;
    private readonly Func<OidcProviderConfig, IConfigurationManager<OpenIdConnectConfiguration>> _configManagerFactory;
    private readonly ConcurrentDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> _configManagers = new(StringComparer.OrdinalIgnoreCase);

    public OidcTokenValidator(
        IConfiguration configuration,
        Func<OidcProviderConfig, IConfigurationManager<OpenIdConnectConfiguration>>? configManagerFactory = null)
    {
        _providers = LoadProviders(configuration);
        _configManagerFactory = configManagerFactory ?? DefaultConfigManagerFactory;
    }

    public IReadOnlyDictionary<string, OidcProviderConfig> Providers => _providers;

    public async Task<OidcValidationResult> ValidateAsync(string providerKey, string idToken, CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(providerKey, out var provider) || !provider.Enabled)
        {
            return OidcValidationResult.Fail("Provider sconosciuto o disattivato.");
        }

        if (!provider.RequiresIdToken)
        {
            return OidcValidationResult.Fail("Questo provider non è configurato per la verifica OIDC (manca Issuer o Audience).");
        }

        if (string.IsNullOrWhiteSpace(idToken))
        {
            return OidcValidationResult.Fail("id_token mancante.");
        }

        var configManager = _configManagers.GetOrAdd(providerKey, _ => _configManagerFactory(provider));

        OpenIdConnectConfiguration oidcConfig;
        try
        {
            oidcConfig = await configManager.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return OidcValidationResult.Fail($"Impossibile ottenere la configurazione del provider: {ex.Message}");
        }

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = provider.Issuer,
            ValidateAudience = true,
            ValidAudience = provider.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(idToken, validationParameters, out _);
        }
        catch (Exception ex)
        {
            return OidcValidationResult.Fail($"Token non valido: {ex.Message}");
        }

        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(subject))
        {
            return OidcValidationResult.Fail("Il token non contiene un identificativo soggetto (sub).");
        }

        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? principal.FindFirstValue(ClaimTypes.Email);
        var name = principal.FindFirstValue("name") ?? principal.FindFirstValue(ClaimTypes.Name);

        return OidcValidationResult.Ok(subject, email, name);
    }

    private static IConfigurationManager<OpenIdConnectConfiguration> DefaultConfigManagerFactory(OidcProviderConfig provider) =>
        new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{provider.Issuer!.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

    private static Dictionary<string, OidcProviderConfig> LoadProviders(IConfiguration configuration)
    {
        var result = new Dictionary<string, OidcProviderConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in configuration.GetSection("Auth:External:Providers").GetChildren())
        {
            var key = child["Key"]?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = new OidcProviderConfig(
                key,
                child["Name"]?.Trim() ?? key,
                child.GetValue("Enabled", true),
                child["Issuer"]?.Trim(),
                child["Audience"]?.Trim());
        }

        return result;
    }
}
