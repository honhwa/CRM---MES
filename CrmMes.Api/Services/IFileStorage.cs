using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>Punto unico da cui passano tutti i file caricati (logo aziendale, foto nei lotti): oggi
/// implementato su database (DbFileStorage), scelta deliberata per restare senza account o servizi esterni
/// finché il volume non lo giustifica. Il giorno in cui servisse uno storage esterno (es. Cloudflare R2),
/// basta una nuova implementazione di questa stessa interfaccia e cambiare la registrazione in
/// Program.cs — nessun controller che la usa deve cambiare.</summary>
public interface IFileStorage
{
    Task<Guid> SaveAsync(byte[] content, string contentType, string? uploadedBy, CancellationToken cancellationToken = default);

    Task<StoredFileContent?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record StoredFileContent(byte[] Content, string ContentType);

public sealed class DbFileStorage(ApplicationDbContext db) : IFileStorage
{
    public async Task<Guid> SaveAsync(byte[] content, string contentType, string? uploadedBy, CancellationToken cancellationToken = default)
    {
        var file = new StoredFile
        {
            ContentType = contentType,
            Content = content,
            SizeBytes = content.Length,
            UploadedBy = uploadedBy,
        };
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        return file.Id;
    }

    public async Task<StoredFileContent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        return file is null ? null : new StoredFileContent(file.Content, file.ContentType);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file is not null)
        {
            db.StoredFiles.Remove(file);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Regole comuni a ogni caricamento immagine, qualunque sia lo scopo (logo, foto lotto): un
/// limite di dimensione basso ha senso finché i file vivono nel database stesso (vedi DbFileStorage),
/// non in uno storage pensato per i file grandi.</summary>
public static class ImageUploadRules
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    public static string? Validate(string? contentType, int sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            return "Formato non supportato: usa JPEG, PNG o WEBP.";
        }

        if (sizeBytes <= 0)
        {
            return "Il file è vuoto.";
        }

        if (sizeBytes > MaxSizeBytes)
        {
            return $"Il file supera il limite di {MaxSizeBytes / 1024 / 1024} MB.";
        }

        return null;
    }
}
