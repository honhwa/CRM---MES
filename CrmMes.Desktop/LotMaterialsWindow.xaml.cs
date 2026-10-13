using System.Net.Http;
using System.Windows;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Materiali previsti per il lotto in lavorazione al terminale di reparto, con quanto è
/// disponibile e quanto manca: la stessa verifica già usata al rilascio della commessa
/// (ApiClient.CheckMaterialAvailabilityAsync), qui aperta su richiesta per monitorare durante il lavoro,
/// non solo al momento del rilascio.</summary>
public partial class LotMaterialsWindow : Window
{
    private sealed class LineRow(MaterialAvailabilityLineDto source)
    {
        public string MaterialCode { get; } = source.MaterialCode;
        public string RequiredLabel { get; } = source.Required.ToString("0.##");
        public string AvailableLabel { get; } = source.Available.ToString("0.##");
        public string ShortfallLabel { get; } = source.Shortfall > 0 ? source.Shortfall.ToString("0.##") : "—";
    }

    public LotMaterialsWindow(ApiClient apiClient, Guid workOrderId, string lotLabel)
    {
        InitializeComponent();
        SubtitleText.Text = lotLabel;
        Loaded += async (_, _) => await LoadAsync(apiClient, workOrderId);
    }

    private async Task LoadAsync(ApiClient apiClient, Guid workOrderId)
    {
        try
        {
            var availability = await apiClient.CheckMaterialAvailabilityAsync(workOrderId);
            LinesList.ItemsSource = availability.Lines.Select(line => new LineRow(line)).ToList();
            if (availability.IsAvailable)
            {
                StatusBanner.Background = Brushes.Transparent;
                StatusText.Foreground = (Brush)FindResource("SuccessTextBrush");
                StatusText.Text = "Tutti i materiali previsti sono disponibili.";
            }
            else
            {
                StatusBanner.Background = (Brush)FindResource("WarningSoftBrush");
                StatusText.Foreground = (Brush)FindResource("WarningTextBrush");
                var missing = availability.Lines.Count(line => line.Shortfall > 0);
                StatusText.Text = $"{missing} {(missing == 1 ? "materiale manca" : "materiali mancano")} rispetto al previsto.";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }
}
