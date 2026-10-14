using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class DataFlowsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public DataFlowsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static SaveDataFlowRequest NotifyThenApproveThenNotify(string firstRole, string approverRole, string secondRole) => new(
        "Prova", DataFlowEvents.EngineeringChangeApplied, true,
        [
            new SaveDataFlowStepRequest(DataFlowStepType.NotifyRole, firstRole, "Modifica {{changeNumber}} su {{productCode}}: in corso."),
            new SaveDataFlowStepRequest(DataFlowStepType.RequireApproval, approverRole, null),
            new SaveDataFlowStepRequest(DataFlowStepType.NotifyRole, secondRole, "Approvata."),
        ]);

    /// <summary>La base dati è condivisa tra i test di questa fixture: un flusso lasciato attivo da un test
    /// si attiverebbe anche nei successivi sullo stesso evento, inviando notifiche inattese. Ogni test che
    /// pubblica l'evento parte quindi da zero flussi configurati.</summary>
    private async Task ResetFlowsAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.DataFlowDefinitions.RemoveRange(await db.DataFlowDefinitions.ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task PublishEngineeringChangeAppliedAsync(string changeNumber = "7", string productCode = "MAC-1")
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<DataFlowEngine>();
        await engine.PublishAsync(DataFlowEvents.EngineeringChangeApplied, "EngineeringChange", Guid.NewGuid(),
            new Dictionary<string, string> { ["changeNumber"] = changeNumber, ["productCode"] = productCode });
    }

    [Fact]
    public async Task OnlyAdmin_CanConfigureFlows_AndRequestsAreValidated()
    {
        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var client = _fixture.Factory.AuthenticatedClient(operator_.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/data-flows",
            NotifyThenApproveThenNotify("Warehouse", "Purchasing", "Warehouse"))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/data-flows",
            new SaveDataFlowRequest("Prova", "evento.inventato", true, [new SaveDataFlowStepRequest(DataFlowStepType.NotifyRole, "Warehouse", "Ciao")]))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/data-flows",
            new SaveDataFlowRequest("Prova", DataFlowEvents.EngineeringChangeApplied, true, []))).StatusCode);

        var events = await _admin.GetFromJsonAsync<List<DataFlowEventResponse>>("/api/data-flows/events");
        Assert.Contains(events!, e => e.Key == DataFlowEvents.EngineeringChangeApplied);
    }

    [Fact]
    public async Task NotifyRole_CreatesANotificationForEveryUserOfThatRole_WithPlaceholdersFilled()
    {
        await ResetFlowsAsync();
        var warehouse = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Warehouse");
        using var warehouseClient = _fixture.Factory.AuthenticatedClient(warehouse.Token);

        var created = await _admin.PostAsJsonAsync("/api/data-flows",
            new SaveDataFlowRequest("Avviso magazzino", DataFlowEvents.EngineeringChangeApplied, true,
                [new SaveDataFlowStepRequest(DataFlowStepType.NotifyRole, "Warehouse", "Modifica {{changeNumber}} su {{productCode}} applicata.")]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await PublishEngineeringChangeAppliedAsync(changeNumber: "42", productCode: "MAC-9");

        var notifications = await warehouseClient.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications");
        Assert.Contains(notifications!, n => n.Message == "Modifica 42 su MAC-9 applicata." && n.ReadAt is null);

        var runs = await _admin.GetFromJsonAsync<List<DataFlowRunResponse>>("/api/data-flows/runs?status=Completed");
        Assert.Contains(runs!, r => r.FlowName == "Avviso magazzino");
    }

    [Fact]
    public async Task RequireApproval_PausesTheRun_AndOnlyTheTargetRoleCanResolveIt()
    {
        await ResetFlowsAsync();
        var warehouse = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Warehouse");
        var purchasing = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Purchasing");
        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var warehouseClient = _fixture.Factory.AuthenticatedClient(warehouse.Token);
        using var purchasingClient = _fixture.Factory.AuthenticatedClient(purchasing.Token);
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operator_.Token);

        (await _admin.PostAsJsonAsync("/api/data-flows", NotifyThenApproveThenNotify("Warehouse", "Purchasing", "Warehouse"))).EnsureSuccessStatusCode();
        await PublishEngineeringChangeAppliedAsync();

        // Il primo avviso è partito; il secondo no, perché il flusso è fermo sull'approvazione.
        var beforeApproval = await warehouseClient.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications");
        Assert.Single(beforeApproval!, n => n.Message.StartsWith("Modifica"));
        Assert.DoesNotContain(beforeApproval!, n => n.Message == "Approvata.");

        var pending = (await _admin.GetFromJsonAsync<List<DataFlowRunResponse>>("/api/data-flows/runs?status=WaitingApproval"))!.Single();
        Assert.Equal("Purchasing", pending.WaitingOnRole);

        // Un ruolo non interessato non può risolverla, né approvare né rifiutare.
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"/api/data-flows/runs/{pending.Id}/approve", null)).StatusCode);

        // L'ufficio acquisti approva: il flusso riprende e completa il secondo avviso.
        var approved = await purchasingClient.PostAsync($"/api/data-flows/runs/{pending.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedRun = await approved.Content.ReadFromJsonAsync<DataFlowRunResponse>();
        Assert.Equal("Completed", approvedRun!.Status);

        var afterApproval = await warehouseClient.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications");
        Assert.Contains(afterApproval!, n => n.Message == "Approvata.");

        // Risolta una volta, non si può approvare o rifiutare una seconda volta.
        Assert.Equal(HttpStatusCode.NotFound, (await purchasingClient.PostAsync($"/api/data-flows/runs/{pending.Id}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Rejecting_StopsTheRunForGood()
    {
        await ResetFlowsAsync();
        var purchasing = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Purchasing");
        using var purchasingClient = _fixture.Factory.AuthenticatedClient(purchasing.Token);

        (await _admin.PostAsJsonAsync("/api/data-flows", NotifyThenApproveThenNotify("Warehouse", "Purchasing", "Warehouse"))).EnsureSuccessStatusCode();
        await PublishEngineeringChangeAppliedAsync();

        var pending = (await _admin.GetFromJsonAsync<List<DataFlowRunResponse>>("/api/data-flows/runs?status=WaitingApproval"))!.Single();
        var rejected = await purchasingClient.PostAsync($"/api/data-flows/runs/{pending.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var rejectedRuns = await _admin.GetFromJsonAsync<List<DataFlowRunResponse>>("/api/data-flows/runs?status=Rejected");
        Assert.Contains(rejectedRuns!, r => r.Id == pending.Id);
    }

    [Fact]
    public async Task AppliedEngineeringChange_PublishesTheRealEvent_EndToEnd()
    {
        await ResetFlowsAsync();
        var warehouse = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Warehouse");
        using var warehouseClient = _fixture.Factory.AuthenticatedClient(warehouse.Token);
        (await _admin.PostAsJsonAsync("/api/data-flows",
            new SaveDataFlowRequest("Avviso vero", DataFlowEvents.EngineeringChangeApplied, true,
                [new SaveDataFlowStepRequest(DataFlowStepType.NotifyRole, "Warehouse", "Prodotto {{productCode}} passato a rev. {{toRevision}}.")]))).EnsureSuccessStatusCode();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var code = $"MAC-{suffix}";
        (await _admin.PostAsJsonAsync("/api/products", new CreateProductRequest(code, $"Macchina {suffix}", null))).EnsureSuccessStatusCode();
        var product = await (await _admin.GetAsync("/api/products")).Content.ReadFromJsonAsync<List<ProductResponse>>();
        var productId = product!.Single(p => p.Code == code).Id;

        var createChange = await _admin.PostAsJsonAsync("/api/engineering/changes",
            new EngineeringChangeRequest(productId, "Prova flussi", null, null, null, [new RoutingStepRequest("Unica", null, null, 10)]));
        createChange.EnsureSuccessStatusCode();
        var change = (await createChange.Content.ReadFromJsonAsync<EngineeringChangeResponse>())!;
        (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/approve", null)).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/apply", null)).EnsureSuccessStatusCode();

        var notifications = await warehouseClient.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications");
        Assert.Contains(notifications!, n => n.Message == $"Prodotto {code} passato a rev. B.");
    }

    /// <summary>Secondo evento cablato dopo quello guida (modifica tecnica), per provare che aggiungerne uno
    /// nuovo è davvero una modifica piccola: emettere un DDT reale deve avvisare chi è configurato.</summary>
    [Fact]
    public async Task IssuingATransportDocument_PublishesTheRealEvent_EndToEnd()
    {
        await ResetFlowsAsync();
        var shipping = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Warehouse");
        using var shippingClient = _fixture.Factory.AuthenticatedClient(shipping.Token);
        (await _admin.PostAsJsonAsync("/api/data-flows",
            new SaveDataFlowRequest("Avviso DDT", DataFlowEvents.TransportDocumentIssued, true,
                [new SaveDataFlowStepRequest(DataFlowStepType.NotifyRole, "Warehouse", "DDT {{documentCode}} emesso per {{recipientName}}.")]))).EnsureSuccessStatusCode();

        var draft = await _admin.PostAsJsonAsync("/api/transport-documents", new SaveTransportDocumentRequest(
            "Sale", null, null, null, "Cliente Flussi spa", "Via Prova 1, Milano", "IT09876543210", null,
            "Sender", null, "Franco", "Scatole", 1, 10m, null, null, null, null,
            [new SaveTransportDocumentLineRequest(null, null, "ART-FLUSSI", "Pezzo di prova", 1, "pz", null, null)]));
        draft.EnsureSuccessStatusCode();
        var document = (await draft.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;

        var issued = await _admin.PostAsJsonAsync($"/api/transport-documents/{document.Id}/issue", new IssueTransportDocumentRequest(null));
        issued.EnsureSuccessStatusCode();
        var issuedDocument = (await issued.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;

        var notifications = await shippingClient.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications");
        Assert.Contains(notifications!, n => n.Message == $"DDT {issuedDocument.DocumentCode} emesso per Cliente Flussi spa.");
    }

    /// <summary>Terzo evento cablato dopo DDT emesso, scelto durante la richiesta esplicita di "iniziare a
    /// crearne alcuni e testarli": una non conformità registrata al terminale di reparto deve avvisare
    /// chi deve saperlo subito (capo reparto/direzione), non al prossimo giro di controllo qualità.</summary>
    [Fact]
    public async Task RegisteringANonConformity_PublishesTheRealEvent_EndToEnd()
    {
        await ResetFlowsAsync();
        var management = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Management");
        using var managementClient = _fixture.Factory.AuthenticatedClient(management.Token);
        (await _admin.PostAsJsonAsync("/api/data-flows",
            new SaveDataFlowRequest("Avviso non conformità", DataFlowEvents.NonConformityReported, true,
                [new SaveDataFlowStepRequest(DataFlowStepType.NotifyRole, "Management",
                    "Non conformità su {{workOrderCode}} ({{productCode}}): {{description}}, {{scrapQuantity}} pezzi scartati.")]))).EnsureSuccessStatusCode();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var materialCode = $"MAT-{suffix}";
        (await _admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(materialCode, $"Materiale {suffix}", "pz", 100, 0))).EnsureSuccessStatusCode();

        var productCode = $"PROD-{suffix}";
        var createProduct = await _admin.PostAsJsonAsync("/api/products", new CreateProductRequest(productCode, "Prodotto prova", null));
        createProduct.EnsureSuccessStatusCode();
        var product = (await createProduct.Content.ReadFromJsonAsync<ProductResponse>())!;

        (await _admin.PutAsJsonAsync($"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(materialCode, 1, null)]))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync($"/api/products/{product.Id}/routing",
            new ReplaceRoutingRequest([new RoutingStepRequest("Assemblaggio", null, "Linea 1", 30)]))).EnsureSuccessStatusCode();

        var createOrder = await _admin.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 2, null, null, null, null, null));
        createOrder.EnsureSuccessStatusCode();
        var order = (await createOrder.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        (await _admin.PostAsJsonAsync($"/api/work-orders/{order.Id}/release", new { })).EnsureSuccessStatusCode();

        var reloaded = await _admin.GetFromJsonAsync<WorkOrderResponse>($"/api/work-orders/{order.Id}");
        var operationId = reloaded!.Operations.Single().Id;
        (await _admin.PostAsync($"/api/work-orders/{order.Id}/operations/{operationId}/start?operatorName=Prova", null))
            .EnsureSuccessStatusCode();

        var register = await _admin.PostAsJsonAsync(
            $"/api/work-orders/{order.Id}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Vite allentata", 1, null));
        register.EnsureSuccessStatusCode();

        var notifications = await managementClient.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications");
        Assert.Contains(notifications!, n => n.Message == $"Non conformità su {order.Code} ({productCode}): Vite allentata, 1 pezzi scartati.");
    }
}
