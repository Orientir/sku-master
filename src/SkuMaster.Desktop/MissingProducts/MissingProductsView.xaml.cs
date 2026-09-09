using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SkuMaster.Core.MissingProducts;

namespace SkuMaster.Desktop.MissingProducts;

public partial class MissingProductsView : UserControl
{
    public MissingProductsViewModel Model { get; }
    private int validationErrors;
    public FrameworkElement DetachActions()
    {
        ModuleFooter.Children.Remove(ActionButtons);
        ActionButtons.DataContext = Model;
        return ActionButtons;
    }
    public MissingProductsView(MissingProductsViewModel model)
    {
        InitializeComponent(); Model = model; DataContext = model;
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Text_Changed));
        AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>((_, e) =>
        {
            validationErrors += e.Action == ValidationErrorEventAction.Added ? 1 : -1;
            Model.InputsValid = validationErrors <= 0;
        }));
    }
    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && e.OriginalSource is TextBox source && InputSettings.IsAncestorOf(source)) Model.Invalidate();
    }
    private void Settings_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Model.Invalidate(); }
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var kind = (string)((Button)sender).Tag;
        var dialog = new OpenFileDialog { Filter = kind == "site" ? "CSV (*.csv)|*.csv" : "Excel (*.xlsx;*.xls)|*.xlsx;*.xls", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (kind == "site") Model.SitePath = dialog.FileName;
        else if (kind == "supplier") Model.SupplierPath = dialog.FileName;
        else await Model.ImportExclusionsAsync(dialog.FileName);
    }
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        await Model.AnalyzeAsync();
        if (Model.HasResult) ModuleTabs.SelectedIndex = 2;
    }
    private void Summary_Click(object sender, RoutedEventArgs e) => Model.SelectSummary(int.Parse((string)((Button)sender).Tag));
    private void Exclusion_Click(object sender, RoutedEventArgs e)
    {
        var check = (CheckBox)sender;
        if (check.DataContext is MissingProductRow row) Model.SetExcluded(row.Sku, check.IsChecked == true);
    }
    private void RemoveExclusions_Click(object sender, RoutedEventArgs e) => Model.RemoveExclusions(ExclusionList.SelectedItems.Cast<string>().ToArray());
    private void Cancel_Click(object sender, RoutedEventArgs e) => Model.Cancel();
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var excel = Model.Settings.OutputFormat == "xlsx";
        var dialog = new SaveFileDialog { Filter = excel ? "Excel (*.xlsx)|*.xlsx" : "CSV (*.csv)|*.csv", DefaultExt = excel ? ".xlsx" : ".csv", AddExtension = true, FileName = "товари_для_сайту", OverwritePrompt = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await Model.ExportAsync(dialog.FileName);
    }
}
