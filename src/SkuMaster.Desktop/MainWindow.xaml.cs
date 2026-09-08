using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkuMaster.Core;
using Microsoft.Win32;

namespace SkuMaster.Desktop;
public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private readonly UpdateService updates = new();
    private readonly Action<string> copyText;
    public MainWindow() : this(null) { }
    public MainWindow(MainViewModel? viewModel, Action<string>? clipboardWriter = null)
    {
        InitializeComponent();
        model = viewModel ?? new();
        copyText = clipboardWriter ?? Clipboard.SetText;
        DataContext = model;
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(SettingsText_Changed));
        VersionLabel.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
    }
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var source = (string)((Button)sender).Tag;
        var dialog = new OpenFileDialog { Filter = source == "site" ? "CSV (*.csv)|*.csv" : "Excel (*.xlsx;*.xls)|*.xlsx;*.xls", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (source == "site") model.SitePath = dialog.FileName;
        else if (source == "onec") model.OneCPath = dialog.FileName;
        else model.SupplierPath = dialog.FileName;
        await model.LoadMetadataAsync(source);
    }
    private void SettingsText_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && !model.IsBusy && e.OriginalSource is DependencyObject source
            && (FilesPanel.IsAncestorOf(source) || SettingsPanel.IsAncestorOf(source))) model.Invalidate();
    }
    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !model.IsBusy) model.Invalidate();
    }
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        await model.AnalyzeAsync();
        if (model.HasResult) Tabs.SelectedIndex = 2;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitFocusedStatus();
        var xlsx = model.Settings.Export.Format == "xlsx";
        var dialog = new SaveFileDialog { Filter = xlsx ? "Excel (*.xlsx)|*.xlsx" : "CSV (*.csv)|*.csv", DefaultExt = xlsx ? ".xlsx" : ".csv", AddExtension = true, OverwritePrompt = true, FileName = Path.GetFileNameWithoutExtension(model.SitePath) + "_оновлено" };
        if (dialog.ShowDialog(this) == true) await model.SaveAsync(dialog.FileName);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => model.Cancel();
    private void ResetFilters_Click(object sender, RoutedEventArgs e) => model.ResetFilters();
    private void Summary_Click(object sender, RoutedEventArgs e)
    {
        CommitFocusedStatus();
        model.SelectSummary(int.Parse((string)((Button)sender).Tag));
    }
    private void CopySku_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not StatusChange row) return;
        try { copyText(row.Sku); model.Message = $"SKU «{row.Sku}» скопійовано."; }
        catch (System.Runtime.InteropServices.ExternalException) { model.Message = "Буфер обміну зайнятий. Натисніть SKU ще раз."; }
    }
    private void CommitFocusedStatus()
    {
        if (Keyboard.FocusedElement is ComboBox { Name: "StatusEditor" } box) CommitStatus(box);
    }
    private void CommitStatus(ComboBox box)
    {
        if (box.DataContext is StatusChange row && box.SelectedValue is string status) model.EditStatus(row.RowNumber, status);
    }
    private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var box = (ComboBox)sender;
        if (box.IsLoaded && !box.IsDropDownOpen) CommitStatus(box);
    }
    private void Status_DropDownClosed(object? sender, EventArgs e) => CommitStatus((ComboBox)sender!);
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CommitFocusedStatus();
        if (model.IsBusy) { model.Message = "Дочекайтеся завершення або скасуйте поточну операцію."; e.Cancel = true; return; }
        if (model.UnsavedResult && MessageBox.Show(this, "Результат ще не збережено. Закрити програму?", "Незбережений результат", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        model.PersistSettings();
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(false);
    private async void Update_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool manual)
    {
        CommitFocusedStatus();
        UpdateButton.IsEnabled = false;
        try
        {
            var ready = await updates.CheckAndDownloadAsync(text => Dispatcher.Invoke(() => UpdateLabel.Text = text));
            if (!ready || !manual) return;
            if (model.IsBusy || model.UnsavedResult) { UpdateLabel.Text = "Спочатку збережіть результат і завершіть обробку."; return; }
            if (MessageBox.Show(this, "Оновлення завантажено. Встановити та перезапустити програму?", "Оновлення", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                model.PersistSettings();
                updates.ApplyAndRestart();
            }
        }
        catch (Exception) { UpdateLabel.Text = "Не вдалося перевірити оновлення. Спробуйте пізніше."; }
        finally { UpdateButton.IsEnabled = true; }
    }
}
