using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

/// <summary>Foto legate a un lotto specifico (controllo qualità, istruzioni operatore), non al prodotto in
/// generale. Vedi WorkOrderAttachmentsController, IFileStorage.</summary>
public class WorkOrderAttachmentsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public WorkOrderAttachmentsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static byte[] TinyPng() =>
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static MultipartFormDataContent AttachmentForm(byte[] bytes, string kind, string? caption = null, string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        var filePart = new ByteArrayContent(bytes);
        filePart.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(filePart, "file", "foto.png");
        form.Add(new StringContent(kind), "kind");
        if (caption is not null)
        {
            form.Add(new StringContent(caption), "caption");
        }

        return form;
    }

    private async Task<Guid> CreateWorkOrderAsync()
    {
        var code = $"PROD-{Guid.NewGuid():N}"[..12];
        var productResponse = await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest(code, "Prodotto di prova", null));
        productResponse.EnsureSuccessStatusCode();
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var orderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        orderResponse.EnsureSuccessStatusCode();
        return (await orderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!.Id;
    }

    [Fact]
    public async Task Upload_UnknownKind_ReturnsBadRequest()
    {
        var workOrderId = await CreateWorkOrderAsync();

        var response = await _adminClient.PostAsync(
            $"/api/work-orders/{workOrderId}/attachments", AttachmentForm(TinyPng(), "inventato"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_UnknownWorkOrder_ReturnsNotFound()
    {
        var response = await _adminClient.PostAsync(
            $"/api/work-orders/{Guid.NewGuid()}/attachments", AttachmentForm(TinyPng(), WorkOrderAttachmentKinds.Quality));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_UnsupportedContentType_ReturnsBadRequest()
    {
        var workOrderId = await CreateWorkOrderAsync();

        var response = await _adminClient.PostAsync(
            $"/api/work-orders/{workOrderId}/attachments", AttachmentForm(TinyPng(), WorkOrderAttachmentKinds.Quality, contentType: "application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ThenList_ThenGetContent_ThenDelete_EndToEnd()
    {
        var workOrderId = await CreateWorkOrderAsync();
        var bytes = TinyPng();

        var upload = await _adminClient.PostAsync(
            $"/api/work-orders/{workOrderId}/attachments",
            AttachmentForm(bytes, WorkOrderAttachmentKinds.Quality, "Vite allentata sul lato destro"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var created = (await upload.Content.ReadFromJsonAsync<WorkOrderAttachmentResponse>())!;
        Assert.Equal(WorkOrderAttachmentKinds.Quality, created.Kind);
        Assert.Equal("Vite allentata sul lato destro", created.Caption);

        var list = await _adminClient.GetFromJsonAsync<List<WorkOrderAttachmentResponse>>($"/api/work-orders/{workOrderId}/attachments");
        Assert.Single(list!, a => a.Id == created.Id);

        var content = await _adminClient.GetAsync($"/api/work-orders/{workOrderId}/attachments/{created.Id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync());

        var delete = await _adminClient.DeleteAsync($"/api/work-orders/{workOrderId}/attachments/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterDelete = await _adminClient.GetFromJsonAsync<List<WorkOrderAttachmentResponse>>($"/api/work-orders/{workOrderId}/attachments");
        Assert.Empty(afterDelete!);
    }

    [Fact]
    public async Task Upload_AsAnyAuthenticatedOperator_Succeeds()
    {
        var workOrderId = await CreateWorkOrderAsync();
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "attach-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsync(
            $"/api/work-orders/{workOrderId}/attachments", AttachmentForm(TinyPng(), WorkOrderAttachmentKinds.Instructions));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetAttachments_WithoutToken_ReturnsUnauthorized()
    {
        var workOrderId = await CreateWorkOrderAsync();
        using var anonymousClient = _fixture.Factory.CreateClient();

        var response = await anonymousClient.GetAsync($"/api/work-orders/{workOrderId}/attachments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
