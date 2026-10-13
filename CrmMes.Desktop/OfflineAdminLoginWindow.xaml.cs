using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Entra senza chiedere nulla al server: verifica il PIN solo contro l'hash salvato su questo
/// PC (vedi OfflineAdminPin). Mostrata solo quando il server non risponde e solo se su questo PC è già
/// stato impostato un PIN offline — un Operatore, o un Admin su un PC dove non l'ha mai impostato, non
/// hanno questa via: per loro serve sempre un vero collegamento al server.</summary>
public partial class OfflineAdminLoginWindow : Window
{
    private readonly string _email;
    private readonly string _hash;
    private readonly string _salt;

    public bool Verified { get; private set; }

    public OfflineAdminLoginWindow(string email, string hash, string salt)
    {
        InitializeComponent();
        _email = email;
        _hash = hash;
        _salt = salt;
        IntroText.Text = $"Il server non risponde. Entra come {email} inserendo il PIN impostato su questo PC: " +
            "potrai correggere l'indirizzo del server e preparare la configurazione aziendale, ma non vedrai i dati reali finché il programma non si ricollega e fai un accesso vero con la tua password.";
        Loaded += (_, _) => PinBox.Focus();
    }

    private void Enter_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (!OfflineAdminPin.Verify(PinBox.Password, _hash, _salt))
        {
            ErrorText.Text = "PIN non corretto.";
            PinBox.Password = string.Empty;
            return;
        }

        Verified = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
