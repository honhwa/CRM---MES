using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Pannello Admin per i flussi di dati (vedi STRUMENTO-FLUSSI-DATI.md): stessa funzionalità della
/// pagina web /flussi-dati, portata sul programma su richiesta esplicita — v1 era solo web.</summary>
public partial class DataFlowsWindow : Window
{
    private readonly ApiClient _apiClient;
    private List<DataFlowEventDto> _events = [];
    private List<string> _roles = [];
    private readonly System.Collections.ObjectModel.ObservableCollection<StepRow> _steps = [];
    private Guid? _editingId;

    public DataFlowsWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        StepsList.ItemsSource = _steps;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _events = (await _apiClient.GetDataFlowEventsAsync()).ToList();
            _roles = (await _apiClient.GetAccessChannelsAsync()).KnownRoles.ToList();
            EventCombo.ItemsSource = _events;
            if (EventCombo.SelectedItem is null && _events.Count > 0)
            {
                EventCombo.SelectedIndex = 0;
            }

            await ReloadFlowsAsync();
            NewFlow();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async Task ReloadFlowsAsync()
    {
        var flows = await _apiClient.GetDataFlowsAsync();
        FlowsList.ItemsSource = flows.Select(f => new FlowRow(f, EventName(f.TriggerEventKey))).ToList();
    }

    private string EventName(string key) => _events.FirstOrDefault(e => e.Key == key)?.Name ?? key;

    private List<DataFlowEventFieldDto> FieldsOf(string? key) => _events.FirstOrDefault(e => e.Key == key)?.Fields ?? [];

    private void NewFlow()
    {
        _editingId = null;
        EditorTitleText.Text = "Nuovo flusso";
        NameBox.Text = string.Empty;
        EnabledBox.IsChecked = true;
        if (_events.Count > 0)
        {
            EventCombo.SelectedIndex = 0;
        }

        _steps.Clear();
        _steps.Add(new StepRow(_roles) { TargetRole = _roles.FirstOrDefault() ?? string.Empty });
        SaveButton.Content = "Crea flusso";
        CancelEditButton.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
        UpdatePlaceholdersText();
    }

    private void EditFlow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FlowRow row })
        {
            return;
        }

        _editingId = row.Source.Id;
        EditorTitleText.Text = "Modifica flusso";
        NameBox.Text = row.Source.Name;
        EnabledBox.IsChecked = row.Source.Enabled;
        EventCombo.SelectedItem = _events.FirstOrDefault(ev => ev.Key == row.Source.TriggerEventKey);

        _steps.Clear();
        foreach (var step in row.Source.Steps.OrderBy(s => s.Order))
        {
            _steps.Add(new StepRow(_roles) { Type = step.Type, TargetRole = step.TargetRole, Message = step.MessageTemplate ?? string.Empty });
        }

        SaveButton.Content = "Salva modifiche";
        CancelEditButton.Visibility = Visibility.Visible;
        ErrorText.Text = string.Empty;
        UpdatePlaceholdersText();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => NewFlow();

    private void EventCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePlaceholdersText();

    private void UpdatePlaceholdersText()
    {
        var key = (EventCombo.SelectedItem as DataFlowEventDto)?.Key;
        var fields = FieldsOf(key);
        PlaceholdersText.Text = fields.Count > 0
            ? "Segnaposto disponibili nel messaggio: " + string.Join(", ", fields.Select(f => $"{{{{{f.Key}}}}} ({f.Label})"))
            : string.Empty;
    }

    private void AddStep_Click(object sender, RoutedEventArgs e) =>
        _steps.Add(new StepRow(_roles) { TargetRole = _roles.FirstOrDefault() ?? string.Empty });

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StepRow row } && _steps.Count > 1)
        {
            _steps.Remove(row);
        }
    }

    private void StepType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Il binding TwoWay su SelectedValue aggiorna già StepRow.Type; qui serve solo rinfrescare
        // IsNotifyRole così il TextBox del messaggio si abilita/disabilita di conseguenza.
        if (sender is FrameworkElement { DataContext: StepRow row })
        {
            row.RefreshIsNotifyRole();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Il nome del flusso è obbligatorio.";
            return;
        }

        if (EventCombo.SelectedItem is not DataFlowEventDto selectedEvent)
        {
            ErrorText.Text = "Scegli l'evento che avvia il flusso.";
            return;
        }

        var steps = _steps.Select(s => new SaveDataFlowStepDto(s.Type, s.TargetRole, s.Type == DataFlowStepTypes.NotifyRole ? s.Message : null)).ToList();
        var body = new SaveDataFlowDto(name, selectedEvent.Key, EnabledBox.IsChecked == true, steps);

        SaveButton.IsEnabled = false;
        try
        {
            if (_editingId is { } id)
            {
                await _apiClient.UpdateDataFlowAsync(id, body);
            }
            else
            {
                await _apiClient.CreateDataFlowAsync(body);
            }

            await ReloadFlowsAsync();
            NewFlow();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void ToggleFlow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FlowRow row })
        {
            return;
        }

        try
        {
            var flow = row.Source;
            var body = new SaveDataFlowDto(flow.Name, flow.TriggerEventKey, !flow.Enabled,
                flow.Steps.OrderBy(s => s.Order).Select(s => new SaveDataFlowStepDto(s.Type, s.TargetRole, s.MessageTemplate)).ToList());
            await _apiClient.UpdateDataFlowAsync(flow.Id, body);
            await ReloadFlowsAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void DeleteFlow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FlowRow row })
        {
            return;
        }

        if (MessageBox.Show(this, $"Eliminare il flusso \"{row.Name}\"?", "Flussi di dati", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteDataFlowAsync(row.Source.Id);
            await ReloadFlowsAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private sealed class FlowRow(DataFlowDto source, string eventName)
    {
        public DataFlowDto Source { get; } = source;
        public string Name { get; } = source.Name;
        public string EventName { get; } = eventName;
        public int StepsCount { get; } = source.Steps.Count;
        public string StatusLabel { get; } = source.Enabled ? "Attivo" : "Disattivo";
        public string ToggleLabel { get; } = source.Enabled ? "Disattiva" : "Attiva";
    }

    private sealed class StepRow(List<string> roles) : INotifyPropertyChanged
    {
        private string _type = DataFlowStepTypes.NotifyRole;
        private string _targetRole = string.Empty;
        private string _message = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public List<string> Roles { get; } = roles;

        public string Type
        {
            get => _type;
            set
            {
                _type = value;
                Notify(nameof(Type));
                RefreshIsNotifyRole();
            }
        }

        public string TargetRole
        {
            get => _targetRole;
            set { _targetRole = value; Notify(nameof(TargetRole)); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; Notify(nameof(Message)); }
        }

        public bool IsNotifyRole => _type == DataFlowStepTypes.NotifyRole;

        public void RefreshIsNotifyRole() => Notify(nameof(IsNotifyRole));

        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
