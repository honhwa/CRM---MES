using System.Security.Cryptography;

namespace CrmMes.Desktop;

/// <summary>PIN per l'accesso locale dell'Admin quando il server non è raggiungibile: unico caso in cui
/// il programma verifica un'identità senza chiedere nulla all'API, perché l'API è proprio il problema.
/// Calcolato e verificato interamente su questo PC (PBKDF2/SHA-256, come fa il server per le password, ma
/// qui il confronto è locale): non esiste un modo di ottenere lo stesso risultato senza il PIN giusto, ma
/// non sostituisce una vera password del server, e infatti dà accesso solo in modalità offline limitata,
/// mai a una sessione autenticata vera (vedi MainWindow.CompleteOfflineAdminLogin).</summary>
public static class OfflineAdminPin
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    public static bool IsValidFormat(string? pin) => pin is { Length: >= 4 and <= 8 } && pin.All(char.IsDigit);

    public static (string Hash, string Salt) Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string pin, string storedHash, string storedSalt)
    {
        try
        {
            var salt = Convert.FromBase64String(storedSalt);
            var expected = Convert.FromBase64String(storedHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
