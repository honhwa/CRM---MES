using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

/// <summary>Prima ondata di validazione input sistematica (14/10/2026, trovata mancante durante il
/// giro "livello enterprise"): vincoli di lunghezza e formato allineati alle colonne del database
/// (vedi ApplicationDbContext) su 4 controller ad alto traffico. Non sostituisce i controlli manuali
/// esistenti (restano, vedi i singoli controller) — aggiunge solo un 400 pulito prima che un input
/// fuori misura arrivi al database, dove oggi fallirebbe con un errore Postgres poco chiaro (500).
/// Le restanti decine di controller sono backlog esplicito in STATO-PROGETTO.md.</summary>
public class InputValidationTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _adminClient;

    public InputValidationTests(AdminSeededApiTestFixture fixture)
    {
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static string LongString(int length) => new('x', length);

    [Fact]
    public async Task CreateMaterial_NomeTroppoLungo_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], LongString(251), "pz", 10, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_GiacenzaNegativa_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], "Materiale di prova", "pz", -1, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_ScortaMinimaNegativa_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], "Materiale di prova", "pz", 10, -5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_ValoriValidi_PassaAncoraLaValidazione()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], "Materiale di prova", "pz", 10, 0));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateProduct_DescrizioneTroppoLunga_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Guid.NewGuid().ToString("N")[..10], "Prodotto di prova", LongString(1001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveCustomer_EmailNonValida_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest("Cliente di prova", Guid.NewGuid().ToString("N")[..10], null, "non-e-una-email", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveCustomer_PartitaIvaTroppoLunga_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest("Cliente di prova", Guid.NewGuid().ToString("N")[..10], LongString(41), null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_EmailNonValida_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Operatore di prova", "non-e-una-email", "Password123!", "Operator"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
