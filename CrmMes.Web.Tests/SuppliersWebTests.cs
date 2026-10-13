using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class SuppliersWebTests : TestContext
{
    private readonly FakeServer _server = new();

    public SuppliersWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/api/suppliers", new List<Supplier>());
    }

    private async Task<IRenderedComponent<Suppliers>> OpenCreateFormAsync(string role, List<CustomFieldDefinition>? customFields = null)
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));
        _server.OnJson("GET", "/api/layout/suppliers.new/custom-fields", customFields ?? []);
        var page = RenderComponent<Suppliers>();
        page.WaitForAssertion(() => Assert.Contains("Nuovo fornitore", page.Markup));
        page.FindAll("button").Single(b => b.TextContent == "Nuovo fornitore").Click();
        return page;
    }

    [Fact]
    public async Task CreateButton_IsHiddenForRolesWithoutPurchasingAccess()
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", "Sales"));
        var page = RenderComponent<Suppliers>();
        page.WaitForAssertion(() => Assert.DoesNotContain("Nuovo fornitore", page.Markup));
    }

    [Fact]
    public async Task CreateButton_IsVisibleForPurchasing()
    {
        var page = await OpenCreateFormAsync("Purchasing");
        Assert.Contains("Crea fornitore", page.Markup);
    }

    [Fact]
    public async Task MissingRequiredField_BlocksSubmission()
    {
        var page = await OpenCreateFormAsync("Admin");

        page.Find("input[placeholder='Ragione sociale']").Change("Fornitore prova");
        page.FindAll("button").Single(b => b.TextContent == "Crea fornitore").Click();

        page.WaitForAssertion(() => Assert.Contains("Compila i campi obbligatori: Codice", page.Markup));
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ValidSubmission_CreatesSupplierAndNavigatesToDetail()
    {
        var newId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Purchasing");
        _server.OnJson("POST", "/api/suppliers", new Supplier(newId, "Fornitore prova", "FOR-900", null, null, null, true));

        page.Find("input[placeholder='Ragione sociale']").Change("Fornitore prova");
        page.Find("input[placeholder='es. FOR-001']").Change("FOR-900");
        page.FindAll("button").Single(b => b.TextContent == "Crea fornitore").Click();

        page.WaitForAssertion(() => Assert.EndsWith($"fornitori/{newId}", Services.GetRequiredService<NavigationManager>().Uri));
        var post = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.PathAndQuery == "/api/suppliers");
        Assert.Contains("\"code\":\"FOR-900\"", post.Body);
    }

    [Fact]
    public async Task CustomFieldValue_IsSentWithTheCreateRequest()
    {
        var fieldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Admin",
        [
            new CustomFieldDefinition(fieldId, "referente-commerciale", "Referente commerciale", "Text", 1, false),
        ]);
        page.WaitForAssertion(() => Assert.Contains("Referente commerciale", page.Markup));
        _server.OnJson("POST", "/api/suppliers", new Supplier(newId, "Fornitore prova", "FOR-901", null, null, null, true));

        page.Find("input[placeholder='Ragione sociale']").Change("Fornitore prova");
        page.Find("input[placeholder='es. FOR-001']").Change("FOR-901");
        page.Find("input[aria-label='Referente commerciale']").Input("Mario Rossi");
        page.FindAll("button").Single(b => b.TextContent == "Crea fornitore").Click();

        page.WaitForAssertion(() => Assert.EndsWith($"fornitori/{newId}", Services.GetRequiredService<NavigationManager>().Uri));
        var post = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.PathAndQuery == "/api/suppliers");
        Assert.Contains("\"referente-commerciale\":\"Mario Rossi\"", post.Body);
    }
}
