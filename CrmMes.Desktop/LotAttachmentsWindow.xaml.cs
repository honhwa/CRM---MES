using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CrmMes.Desktop;

/// <summary>Foto legate a questo lotto (controllo qualità, istruzioni operatore) — non il prodotto in
/// generale, per quello esistono già i documenti tecnici. Vedi WorkOrderAttachmentsController.</summary>
public partial class LotAttachmentsWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;

    private sealed class AttachmentRow
    {
        public Guid Id { get; init; }
        public BitmapImage? Thumbnail { get; init; }
        public string KindLabel { get; init; } = string.Empty;
        public string? Caption { get; init; }
        public Visibility CaptionVisibility => string.IsNullOrWhiteSpace(Caption) ? Visibility.Collapsed : Visibility.Visible;
        public string UploadedLabel { get; init; } = string.Empty;
    }

    public LotAttachmentsWindow(ApiClient apiClient, Guid workOrderId, string lotLabel)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        SubtitleText.Text = lotLabel;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        ErrorText.Text = string.Empty;
        try
        {
            var attachments = await _apiClient.GetWorkOrderAttachmentsAsync(_workOrderId);
            var rows = new List<AttachmentRow>();
            foreach (var attachment in attachments)
            {
                BitmapImage? thumbnail = null;
                try
                {
                    var bytes = await _apiClient.GetWorkOrderAttachmentContentAsync(_workOrderId, attachment.Id);
                    thumbnail = ToImage(bytes);
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
                {
                    // Una singola foto non caricabile non deve impedire di vedere le altre.
                }

                rows.Add(new AttachmentRow
                {
                    Id = attachment.Id,
                    Thumbnail = thumbnail,
                    KindLabel = attachment.Kind == WorkOrderAttachmentKinds.Instructions ? "Istruzioni operatore" : "Controllo qualità",
                    Caption = attachment.Caption,
                    UploadedLabel = $"{attachment.UploadedAt.ToLocalTime():dd/MM/yyyy HH:mm}{(attachment.UploadedBy is null ? "" : $" · {attachment.UploadedBy}")}",
                });
            }

            AttachmentsList.ItemsSource = rows;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private static BitmapImage ToImage(byte[] bytes)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Scegli una foto",
            Filter = "Immagini (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ErrorText.Text = string.Empty;
        UploadButton.IsEnabled = false;
        try
        {
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var contentType = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg",
            };
            var kind = (KindCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? WorkOrderAttachmentKinds.Quality;
            await _apiClient.UploadWorkOrderAttachmentAsync(_workOrderId, bytes, contentType, Path.GetFileName(dialog.FileName), kind, CaptionBox.Text, default);
            CaptionBox.Text = string.Empty;
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private async void DeleteAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AttachmentRow row })
        {
            return;
        }

        if (MessageBox.Show(this, "Eliminare questa foto?", "Foto del lotto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteWorkOrderAttachmentAsync(_workOrderId, row.Id);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }
}
