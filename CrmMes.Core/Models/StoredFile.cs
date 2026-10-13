namespace CrmMes.Core.Models;

/// <summary>Un file binario (oggi solo immagini: logo aziendale, foto di controllo qualità/istruzioni
/// operatore su un lotto) tenuto dentro il database. Scelta deliberata per oggi — vedi
/// CrmMes.Api/Services/IFileStorage per il punto in cui passare a uno storage esterno (es. Cloudflare R2)
/// quando il volume lo giustificherà, senza dover cambiare chi lo usa.</summary>
public class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
    public int SizeBytes { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
