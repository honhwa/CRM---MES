using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class CapasWebTests : TestContext
{
    private readonly FakeServer _server = new();

    public CapasWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    private static CapaRow Capa(string status = "Open", DateTime? dueDate = null) => new(
        Guid.NewGuid(), "CAPA-2026-0001", null, "Spessore fuori tolleranza", "Riscontrato in collaudo", status,
        DateTime.UtcNow, dueDate, status == "Closed" ? DateTime.UtcNow : null, null, null, null);

    [Fact]
    public async Task Operator_CanSeeTheList_ButNotTheNewCapaForm()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", "/api/capas", new List<CapaRow> { Capa() });

        var page = RenderComponent<Capas>();

        page.WaitForAssertion(() => Assert.Contains("CAPA-2026-0001", page.Markup));
        Assert.DoesNotContain("Nuova CAPA", page.Markup);
    }

    [Fact]
    public async Task CreatingACapa_WithoutATitle_IsRefused_AndSendsNothing()
    {
        await LogInAsync("Management");
        _server.OnJson("GET", "/api/capas", new List<CapaRow>());

        var page = RenderComponent<Capas>();
        page.WaitForAssertion(() => Assert.Contains("Nuova CAPA", page.Markup));

        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Contains("Compila i campi obbligatori: Titolo", page.Markup));
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AdminRelabelsAndHidesTheCodeField_AndTheFormFollowsTheLayout()
    {
        await LogInAsync("Admin");
        _server.OnJson("GET", "/api/capas", new List<CapaRow>());
        _server.OnJson("GET", "/api/layout/capa.new", new LayoutScreen("capa.new", "Nuova azione correttiva (CAPA, qualità)",
        [
            new("title", "Oggetto della non conformità", 1, true, true, false),
            new("description", "Analisi", 2, true, false, true),
            new("dueDate", "Da chiudere entro", 3, true, false, true),
            new("code", "Codice (opzionale)", 4, false, false, true),
        ]));
        _server.OnJson("POST", "/api/capas", Capa());

        var page = RenderComponent<Capas>();
        page.WaitForAssertion(() => Assert.Contains("Oggetto della non conformità", page.Markup));

        // Il codice è nascosto dal layout: niente campo per inserirlo.
        Assert.DoesNotContain("Codice (opzionale)", page.Markup);

        page.Find("input").Change("Spessore fuori tolleranza");
        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath == "/api/capas"));
        var created = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath == "/api/capas");
        Assert.Contains("Spessore fuori tolleranza", created.Body);
    }
}
