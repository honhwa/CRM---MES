namespace CrmMes.Core.Models;

/// <summary>Una foto legata a un lotto/commessa specifico — non al prodotto in generale (per quello
/// esiste già TechnicalDocument): "quality" per documentare lo stato reale di questo lotto durante il
/// controllo qualità, "instructions" per un'indicazione operativa pensata apposta per questo lotto.</summary>
public class WorkOrderAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;

    /// <summary>"quality" o "instructions" (vedi WorkOrderAttachmentKinds).</summary>
    public string Kind { get; set; } = WorkOrderAttachmentKinds.Quality;
    public string? Caption { get; set; }

    public Guid FileId { get; set; }
    public StoredFile File { get; set; } = null!;

    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public static class WorkOrderAttachmentKinds
{
    public const string Quality = "quality";
    public const string Instructions = "instructions";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Quality] = "Controllo qualità",
        [Instructions] = "Istruzioni operatore",
    };

    public static bool IsKnown(string? kind) => kind is not null && Labels.ContainsKey(kind);
}
