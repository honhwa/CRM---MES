using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Chiesta una sola volta per PC, in due momenti possibili: al primo avvio dopo l'installazione
/// (prima ancora di collegarsi a un server: label generica, nessuna email reale esiste ancora) oppure
/// subito dopo un vero login online come Admin (label = la sua email reale). Facoltativa in entrambi i
/// casi: "Non ora" chiude senza salvare nulla e il programma richiede di nuovo la stessa scelta al
/// prossimo momento utile, finché un PIN non viene impostato su questo PC.</summary>
public partial class OfflineAdminPinSetupWindow : Window
{
    private const string GenericLabel = "Amministratore (accesso locale)";

    private readonly string _email;

    public bool PinSet { get; private set; }

    /// <summary>email: null al primo avvio, quando ancora non esiste un vero account collegato a un
    /// server — il PIN resta comunque valido, solo con un'etichetta generica invece della sua email.</summary>
    public OfflineAdminPinSetupWindow(string? email = null)
    {
        InitializeComponent();
        _email = string.IsNullOrWhiteSpace(email) ? GenericLabel : email;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var pin = PinBox.Password;
        if (!OfflineAdminPin.IsValidFormat(pin))
        {
            ErrorText.Text = "Il PIN deve essere numerico, da 4 a 8 cifre.";
            return;
        }

        if (pin != ConfirmPinBox.Password)
        {
            ErrorText.Text = "I due PIN non coincidono.";
            return;
        }

        var (hash, salt) = OfflineAdminPin.Hash(pin);
        var settings = ClientSettings.Load();
        settings.OfflineAdminEmail = _email;
        settings.OfflineAdminPinHash = hash;
        settings.OfflineAdminPinSalt = salt;
        settings.Save();

        PinSet = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();
}
