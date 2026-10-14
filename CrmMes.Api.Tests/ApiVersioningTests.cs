using System.Net;
using CrmMes.Api.Services;
using Microsoft.AspNetCore.Http;

namespace CrmMes.Api.Tests;

/// <summary>/api/v1/ è un alias additivo (14/10/2026, roadmap "livello enterprise"): riscritto
/// internamente in /api/ prima di qualunque controllo, cosi' i client esistenti (desktop/web/console)
/// restano invariati e un futuro integratore esterno ha un prefisso stabile. Qui: la riscrittura del
/// percorso isolata (unitaria) e una chiamata reale end-to-end per provare che si comporta in tutto e
/// per tutto come /api/.</summary>
public class ApiVersioningTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public ApiVersioningTests(AdminSeededApiTestFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("/api/v1/materials", "/api/materials")]
    [InlineData("/api/v1/users/123", "/api/users/123")]
    [InlineData("/api/v1", "/api")]
    [InlineData("/api/v1/", "/api/")]
    public async Task RiscriveApiV1_InApi(string input, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = input;
        PathString? seen = null;

        await ApiVersionRewriteMiddleware.InvokeAsync(context, ctx =>
        {
            seen = ctx.Request.Path;
            return Task.CompletedTask;
        });

        Assert.Equal(expected, seen!.Value.Value);
    }

    [Theory]
    [InlineData("/api/materials")]
    [InlineData("/api/v10/materials")]
    [InlineData("/health")]
    [InlineData("/app/v1/materials")]
    public async Task NonTocca_PercorsiCheNonSonoApiV1(string input)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = input;
        PathString? seen = null;

        await ApiVersionRewriteMiddleware.InvokeAsync(context, ctx =>
        {
            seen = ctx.Request.Path;
            return Task.CompletedTask;
        });

        Assert.Equal(input, seen!.Value.Value);
    }

    [Fact]
    public async Task EndToEnd_ApiV1Users_SiComportaComeApiUsers()
    {
        var client = _fixture.Factory.AuthenticatedClient(_fixture.Admin.Token);

        var viaV1 = await client.GetAsync("/api/v1/users");
        var viaDiretta = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, viaV1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viaDiretta.StatusCode);
        Assert.Equal(await viaDiretta.Content.ReadAsStringAsync(), await viaV1.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EndToEnd_ApiV1SenzaToken_RifiutaComeApiDiretta()
    {
        using var anonymousClient = _fixture.Factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
