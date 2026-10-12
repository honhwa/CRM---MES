using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

public partial class CreateMaterialWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    private IReadOnlyList<LayoutFieldDto> _layout = [];
    private IReadOnlyList<CustomFieldDefinitionDto> _customFieldDefinitions = [];
    private List<CustomFieldControl> _customFieldControls = [];

    public CreateMaterialWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) =>
        {
            await LoadLayoutAsync();
            await LoadCustomFieldsAsync();
        };
    }

    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "materials.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("code", CodeBox),
            new FieldBinding("name", NameBox),
            new FieldBinding("unit", UnitBox),
            new FieldBinding("stock", StockBox),
            new FieldBinding("minStock", MinStockBox),
        ]);
    }

    /// <summary>Campi personalizzati aggiunti dall'Admin alla schermata "Nuovo materiale" (stesso meccanismo
    /// già collegato a fornitori e clienti): costruiti a runtime, nessun valore precompilato in creazione.</summary>
    private async Task LoadCustomFieldsAsync()
    {
        _customFieldDefinitions = await CustomFieldForm.LoadAsync(_apiClient, "materials.new");
        _customFieldControls = CustomFieldForm.BuildControls(CustomFieldsPanel, _customFieldDefinitions, values: null);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        var name = NameBox.Text.Trim();
        var unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? "pz" : UnitBox.Text.Trim();

        var customFieldValues = CustomFieldForm.ToValues(_customFieldControls);

        var missing = FormLayoutApplier.Missing(_layout, key => key switch
        {
            "code" => !string.IsNullOrWhiteSpace(code),
            "name" => !string.IsNullOrWhiteSpace(name),
            "unit" => !string.IsNullOrWhiteSpace(UnitBox.Text),
            "stock" => !string.IsNullOrWhiteSpace(StockBox.Text),
            "minStock" => !string.IsNullOrWhiteSpace(MinStockBox.Text),
            _ => true,
        });
        missing.AddRange(CustomFieldForm.Missing(_customFieldDefinitions, customFieldValues));
        if (missing.Count > 0)
        {
            ErrorText.Text = "Compila i campi obbligatori: " + string.Join(", ", missing) + ".";
            return;
        }

        if (!NumberInput.TryParseDecimal(StockBox.Text, out var stock) || stock < 0 ||
            !NumberInput.TryParseDecimal(MinStockBox.Text, out var minStock) || minStock < 0)
        {
            ErrorText.Text = "Giacenza e scorta minima devono essere numeri non negativi.";
            return;
        }

        var typeError = CustomFieldForm.Validate(_customFieldDefinitions, customFieldValues);
        if (typeError is not null)
        {
            ErrorText.Text = typeError;
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            await _apiClient.CreateMaterialAsync(code, name, unit, stock, minStock, customFieldValues);
            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
