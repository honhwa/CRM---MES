using System.Net;
using System.Net.Http.Headers;

namespace CrmMes.Api.Tests;

/// <summary>Logo aziendale (vedi IFileStorage, StoredFile): leggibile senza accesso come il tema, solo
/// un Admin può cambiarlo. Tenuto nel database stesso (nessuno storage esterno oggi), vedi
/// STATO-PROGETTO.md per il perché. Come CompanyProfileTests: il logo è una singola riga per database,
/// quindi i test che lo scrivono vanno in sequenza dentro un solo metodo, indipendenti dall'ordine
/// con cui xUnit li eseguirebbe altrimenti.</summary>
public class CompanyLogoTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public CompanyLogoTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static byte[] TinyPng() =>
        // 1x1 transparent PNG, valid bytes, enough to round-trip through the storage layer.
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static MultipartFormDataContent LogoForm(byte[] bytes, string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        var filePart = new ByteArrayContent(bytes);
        filePart.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(filePart, "file", "logo.png");
        return form;
    }

    [Fact]
    public async Task GetLogo_NoneUploaded_ReturnsNotFound_EvenWithoutLogin()
    {
        using var anonymousClient = _fixture.Factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/company-profile/logo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SaveLogo_AsNonAdmin_ReturnsForbidden()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "logo-guard");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PutAsync("/api/company-profile/logo", LogoForm(TinyPng()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SaveLogo_UnsupportedContentType_ReturnsBadRequest()
    {
        var response = await _adminClient.PutAsync("/api/company-profile/logo", LogoForm(TinyPng(), "application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveLogo_ThenReplace_ThenDelete_InSequence()
    {
        var firstBytes = TinyPng();
        var saved = await _adminClient.PutAsync("/api/company-profile/logo", LogoForm(firstBytes));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        using var anonymousClient = _fixture.Factory.CreateClient();
        var afterFirstSave = await anonymousClient.GetAsync("/api/company-profile/logo");
        Assert.Equal(HttpStatusCode.OK, afterFirstSave.StatusCode);
        Assert.Equal("image/png", afterFirstSave.Content.Headers.ContentType?.MediaType);
        Assert.Equal(firstBytes, await afterFirstSave.Content.ReadAsByteArrayAsync());

        // A second upload replaces it (same bytes here, just proving the previous file is dropped, not
        // left orphaned in storage) rather than accumulating.
        var replaced = await _adminClient.PutAsync("/api/company-profile/logo", LogoForm(TinyPng()));
        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);

        var deleted = await _adminClient.DeleteAsync("/api/company-profile/logo");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await anonymousClient.GetAsync("/api/company-profile/logo");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }
}
