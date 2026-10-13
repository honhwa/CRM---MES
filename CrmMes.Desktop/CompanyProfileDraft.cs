using System.IO;
using System.Text.Json;

namespace CrmMes.Desktop;

/// <summary>Quanto inserito in "Azienda e settore" mentre si è in accesso amministratore offline: il
/// server non c'è, quindi non si può salvare davvero. Resta qui, solo su questo PC, finché un vero
/// accesso online non lo invia con le stesse tre chiamate che userebbe un salvataggio normale (vedi
/// MainWindow.OfferToSendCompanyProfileDraftAsync).</summary>
public sealed record CompanyProfileDraft(
    SaveCompanyProfileDto Profile,
    List<SaveDepartmentDto> Departments,
    string? Gs1CompanyPrefix,
    DateTime SavedAtUtc)
{
    private static readonly string DraftPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrmMes",
        "company-profile-draft.json");

    public static CompanyProfileDraft? Load() => Load(DraftPath);

    /// <summary>Test seam: stesso schema di ClientSettings.Load(path), per non toccare mai il vero
    /// profilo utente durante un run di test.</summary>
    internal static CompanyProfileDraft? Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<CompanyProfileDraft>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null; // bozza corrotta o illeggibile: meglio ignorarla che bloccare il login
        }
    }

    public void Save() => Save(DraftPath);

    internal void Save(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete() => Delete(DraftPath);

    internal static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // non critico: resterebbe solo un file locale orfano, riproposto senza danno al prossimo avvio
        }
    }
}

/// <summary>Ultimo catalogo (settori, moduli, reparti tipici) letto online, per poter almeno mostrare
/// "Azienda e settore" quando si è offline: senza questo, la finestra non avrebbe nulla da elencare.</summary>
public static class CompanyCatalogCache
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrmMes",
        "company-catalog-cache.json");

    public static CompanyCatalogDto? Load()
    {
        try
        {
            return File.Exists(CachePath)
                ? JsonSerializer.Deserialize<CompanyCatalogDto>(File.ReadAllText(CachePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(CompanyCatalogDto catalog)
    {
        try
        {
            var directory = Path.GetDirectoryName(CachePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(catalog));
        }
        catch
        {
            // la cache è solo una comodità per l'offline: se non si riesce a scrivere, si continua senza
        }
    }
}
