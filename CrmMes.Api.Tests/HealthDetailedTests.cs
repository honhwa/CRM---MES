using System.Net;
using System.Text.Json;

namespace CrmMes.Api.Tests;

public class HealthDetailedTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public HealthDetailedTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HealthDetailed_IsAnonymousAndReportsPostgresCheck()
    {
        var response = await _fixture.Client.GetAsync("/health/detailed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "postgres"
            && check.GetProperty("status").GetString() == "Healthy");
    }

    /// <summary>Trovato durante il backtest di sicurezza del 14/10/2026: /health è fuori da /api, quindi
    /// non prendeva le intestazioni di difesa in profondità che il resto delle rotte ha già (X-Frame-Options
    /// ecc.) — niente di sensibile in risposta, ma nessuna ragione per fare un'eccezione qui.</summary>
    [Fact]
    public async Task Health_CarriesTheSameDefenseInDepthHeaders_AsTheRestOfTheApi()
    {
        var response = await _fixture.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }
}
