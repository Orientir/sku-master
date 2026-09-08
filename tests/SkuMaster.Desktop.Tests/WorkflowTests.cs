using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NPOI.XSSF.UserModel;
using SkuMaster.Core;
using SkuMaster.Desktop;
using SkuMaster.Infrastructure;
using Xunit;

namespace SkuMaster.Desktop.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task ReasonsIdentifyEachSourceAndSurviveManualEdits()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SkuMaster-sources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            CreateInputs(dir);
            File.WriteAllText(Path.Combine(dir, "site.csv"), "001;Временно недоступен\n002;Временно недоступен\n003;Временно недоступен\n004;Новинка\n005;\n 003 ;Временно недоступен\n", new UTF8Encoding(true));
            WriteBook(Path.Combine(dir, "onec.xlsx"), "001", "003", "004");
            WriteBook(Path.Combine(dir, "supplier.xlsx"), "002", "003");
            var model = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")))
            {
                SitePath = Path.Combine(dir, "site.csv"), OneCPath = Path.Combine(dir, "onec.xlsx"), SupplierPath = Path.Combine(dir, "supplier.xlsx")
            };
            await model.AnalyzeAsync();
            Assert.True(model.HasResult, model.Message);
            Assert.Equal("Знайдено в 1С", model.Changes.Single(x => x.Sku == "001").Reason);
            Assert.Equal("Знайдено у постачальника", model.Changes.Single(x => x.Sku == "002").Reason);
            Assert.Equal("Знайдено в 1С та у постачальника", model.Changes.Single(x => x.Sku == "003").Reason);
            Assert.Equal("Знайдено в 1С та у постачальника", model.Changes.Single(x => x.Sku == " 003 ").Reason);
            Assert.Equal("Немає в 1С та у постачальника", model.Changes.Single(x => x.Sku == "005").Reason);
            model.SelectSummary(4);
            Assert.Equal("Без змін · Знайдено в 1С", Assert.Single(model.Changes).Reason);
            model.EditStatus(4, "Топ продаж");
            model.SelectSummary(0);
            Assert.Equal("Змінено вручну · Знайдено в 1С", model.Changes.Single(x => x.Sku == "004").Reason);
            model.EditStatus(3, "Временно недоступен");
            model.SelectSummary(3);
            Assert.Equal("Змінено вручну · Знайдено в 1С та у постачальника", model.Changes.Single(x => x.Sku == "003").Reason);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task SummaryFiltersAndManualEditsUpdateStatisticsAndSavedRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SkuMaster-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            CreateInputs(dir);
            var model = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")))
            {
                SitePath = Path.Combine(dir, "site.csv"), OneCPath = Path.Combine(dir, "onec.xlsx"), SupplierPath = Path.Combine(dir, "supplier.xlsx")
            };
            await model.AnalyzeAsync();
            model.SearchText = "no matches";
            model.SelectSummary(1);
            Assert.Equal("003", Assert.Single(model.Changes).Sku);
            model.SelectSummary(2);
            Assert.Equal(2, model.Changes.Count);
            model.SelectSummary(3);
            Assert.Equal(5, model.Changes.Count);
            model.SelectSummary(4);
            Assert.Equal(new[] { "004", "005" }, model.Changes.Select(x => x.Sku));
            await model.SaveAsync(Path.Combine(dir, "before.csv"));
            Assert.False(model.UnsavedResult);
            model.EditStatus(4, "Новинка");
            Assert.True(model.UnsavedResult);
            Assert.Equal(4, model.Summary!.Changed);
            Assert.Equal("005", Assert.Single(model.Changes).Sku);
            model.SelectSummary(0);
            Assert.Equal("Новинка", model.Changes.Single(x => x.Sku == "004").NewStatus);
            Assert.Contains(model.NewStatusOptions, x => x.Value == "Новинка");
            model.EditStatus(1, "Временно недоступен");
            model.EditStatus(2, "Топ продаж");
            model.SearchText = "003";
            model.ExportOnlyChanged = true;
            await model.SaveAsync(Path.Combine(dir, "after.csv"));
            var saved = new FileService().ReadSite(Path.Combine(dir, "after.csv"), new() { HasHeader = true });
            Assert.Equal(new[] { "002", "003", "004" }, saved.Rows.Select(x => x[0]));
            Assert.Equal("Топ продаж", saved.Rows[0][1]);
            Assert.Equal("Новинка", saved.Rows[2][1]);
            Assert.True(model.CanSave);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task FullWorkflowUnionsBothSourcesExportsAndDetectsLaterChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SkuMaster-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            CreateInputs(dir);
            var model = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")))
            {
                SitePath = Path.Combine(dir, "site.csv"), OneCPath = Path.Combine(dir, "onec.xlsx"), SupplierPath = Path.Combine(dir, "supplier.xlsx")
            };
            await model.AnalyzeAsync();
            Assert.True(model.HasResult, model.Message);
            Assert.Equal(2, model.Summary!.Available);
            Assert.Equal(1, model.Summary.Unavailable);
            Assert.Equal(2, model.Summary.Unchanged);
            Assert.True(model.ExportOnlyChanged);
            model.ExportOnlyChanged = false;
            model.SearchText = "003";
            Assert.Equal("003", Assert.Single(model.Changes).Sku);
            Assert.True(model.CanSave);
            var output = Path.Combine(dir, "result.csv");
            await model.SaveAsync(output);
            Assert.True(File.Exists(output), model.Message);
            Assert.False(model.UnsavedResult);
            var result = new FileService().ReadSite(output, new() { HasHeader = true });
            Assert.Equal(5, result.Rows.Count);
            Assert.Equal("", result.Rows[0][1]);
            Assert.Equal("", result.Rows[1][1]);
            Assert.Equal("Временно недоступен", result.Rows[2][1]);
            Assert.Equal("Акція", result.Rows[3][1]);
            model.ExportOnlyChanged = true;
            var changedPath = Path.Combine(dir, "changed.csv");
            await model.SaveAsync(changedPath);
            Assert.True(File.Exists(changedPath), model.Message);
            var changed = new FileService().ReadSite(changedPath, new() { HasHeader = true });
            Assert.Equal(new[] { "001", "002", "003" }, changed.Rows.Select(x => x[0]));
            Assert.True(new SettingsStore(Path.Combine(dir, "settings.json")).Load().Settings.Export.OnlyChanged);
            model.ExportOnlyChanged = false;
            var fullAgain = Path.Combine(dir, "full-again.csv");
            await model.SaveAsync(fullAgain);
            Assert.Equal(5, new FileService().ReadSite(fullAgain, new() { HasHeader = true }).Rows.Count);
            model.Settings.Rules.ReplacementStatus = "Доступний";
            await model.SaveAsync(Path.Combine(dir, "stale.csv"));
            Assert.False(File.Exists(Path.Combine(dir, "stale.csv")));
            Assert.False(model.HasResult);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WindowRendersFilesSettingsAndActualResultWithoutBindingErrors() => OnSta(async () =>
    {
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var dir = Path.Combine(Path.GetTempPath(), "SkuMaster-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        MainWindow? window = null;
        try
        {
            CreateInputs(dir);
            var model = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")))
            {
                SitePath = Path.Combine(dir, "site.csv"), OneCPath = Path.Combine(dir, "onec.xlsx"), SupplierPath = Path.Combine(dir, "supplier.xlsx")
            };
            string? copiedSku = null;
            window = new MainWindow(model, value => copiedSku = value) { Width = 980, Height = 640, Left = -10000, Top = -10000, ShowActivated = false };
            // Use the window's actual view model so event handlers and bindings share state.
            var actual = (MainViewModel)window.DataContext;
            actual.SitePath = model.SitePath;
            actual.OneCPath = model.OneCPath;
            actual.SupplierPath = model.SupplierPath;
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var tabs = (TabControl)window.FindName("Tabs");
            Assert.All(Descendants<ScrollViewer>(tabs), viewer => Assert.True(viewer.ScrollableHeight == 0, "File selection must fit without vertical scrolling."));
            Render(window, "01-files");
            window.Width = 840;
            window.Height = 580;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var filesPanel = (Grid)window.FindName("FilesPanel");
            foreach (var button in Descendants<Button>(filesPanel))
            {
                var bounds = button.TransformToAncestor(tabs).TransformBounds(new Rect(button.RenderSize));
                Assert.True(bounds.Bottom <= tabs.ActualHeight && bounds.Right <= tabs.ActualWidth,
                    "All three file buttons must remain visible at minimum window size.");
            }
            window.Width = 980;
            window.Height = 640;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            tabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await actual.LoadMetadataAsync("site");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var skuCombo = Descendants<ComboBox>(window).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Колонка SKU");
            var statusCombo = Descendants<ComboBox>(window).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Колонка статусу");
            var headerCheck = Descendants<CheckBox>(window).Single(x => Equals(x.Content, "Перший рядок CSV містить заголовки"));
            Assert.False(skuCombo.IsEnabled);
            headerCheck.IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(skuCombo.IsEnabled, "Enabling CSV headers must enable column selection immediately.");
            Assert.True(statusCombo.IsEnabled);
            headerCheck.IsChecked = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(skuCombo.IsEnabled);
            skuCombo.SelectedIndex = 0;
            statusCombo.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Render(window, "02-settings");
            await actual.AnalyzeAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(actual.HasResult, actual.Message);
            Assert.Equal(3, actual.Summary!.Changed);
            tabs.SelectedIndex = 2;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Render(window, "03-results");
            var search = Descendants<TextBox>(window).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Пошук SKU або статусу");
            search.Text = "002";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(actual.HasResult, "Searching must not invalidate the processed result.");
            Assert.Equal("002", Assert.Single(actual.Changes).Sku);
            actual.Filter = 1;
            Assert.Empty(actual.Changes);
            actual.ResetFilters();
            actual.SelectedNewStatus = actual.NewStatusOptions.Single(x => x.Value == "");
            Assert.Equal(2, actual.Changes.Count);
            var reset = Descendants<Button>(window).Single(x => Equals(x.Content, "Скинути"));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(3, actual.Changes.Count);
            Assert.Equal("", search.Text);
            Assert.True(actual.CanSave, actual.Message);
            var scopeCheck = Descendants<CheckBox>(window).Single(x => Equals(x.Content, "Зберегти лише змінені товари"));
            scopeCheck.IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(actual.ExportOnlyChanged);
            Assert.True(actual.HasResult);
            Assert.True(actual.CanSave);
            scopeCheck.IsChecked = false;
            var summaryCards = Descendants<Button>(window).Where(x => x.Name.StartsWith("SummaryCard")).ToArray();
            Assert.Equal(5, summaryCards.Length);
            foreach (var (tag, count) in new[] { ("1", 1), ("2", 2), ("3", 5), ("4", 2), ("0", 3) })
            {
                actual.SearchText = "nonexistent";
                summaryCards.Single(x => Equals(x.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(count, actual.Changes.Count);
            }
            var editor = Descendants<ComboBox>(window).First(x => x.Name == "StatusEditor");
            var editedRow = (StatusChange)editor.DataContext;
            var copyButton = Descendants<Button>(window).First(x => x.DataContext is StatusChange);
            copyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(((StatusChange)copyButton.DataContext).Sku, copiedSku);
            Assert.False(editor.IsEditable);
            Assert.Equal(new[] { "", "Снят с производства", "Топ продаж", "Лучшая цена", "Скоро в продаже", "Новинка", "Уценка", "%", "Спецпредложение", "Временно недоступен" },
                editor.Items.Cast<Choice>().Select(x => x.Value));
            editor.IsDropDownOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            editor.SelectedValue = "Новинка";
            Assert.Equal(editedRow.NewStatus, actual.Changes.Single(x => x.RowNumber == editedRow.RowNumber).NewStatus);
            editor.IsDropDownOpen = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Новинка", actual.Changes.Single(x => x.RowNumber == editedRow.RowNumber).NewStatus);
            Assert.True(actual.HasResult);
            Assert.NotNull(window.Icon);
            editor = Descendants<ComboBox>(window).First(x => x.Name == "StatusEditor");
            editedRow = (StatusChange)editor.DataContext;
            editor.SelectedValue = "";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("", actual.Changes.Single(x => x.RowNumber == editedRow.RowNumber).NewStatus);
            window.Width = 840;
            window.Height = 580;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Render(window, "04-results-minimum");
            tabs.SelectedIndex = 3;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Render(window, "05-guide-minimum");
            actual.Invalidate();
        }
        finally
        {
            if (window != null) { ((MainViewModel)window.DataContext).Invalidate(); window.Close(); }
            app.Shutdown();
            Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData("https://github.com/owner/repo", true)]
    [InlineData("https://evil.example/owner/repo", false)]
    [InlineData("http://github.com/owner/repo", false)]
    [InlineData("https://github.com/owner/repo?token=secret", false)]
    [InlineData("", false)]
    public void UpdateFeedAcceptsOnlyPublicGitHubRepositoryUrls(string url, bool expected)
        => Assert.Equal(expected, UpdateService.IsValidRepository(url));

    private static void CreateInputs(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "site.csv"), "001;Временно недоступен;Товар із 1С\r\n002;Временно недоступен;Товар постачальника\r\n003;;Відсутній товар\r\n004;Акція;Акційний товар\r\n005;Временно недоступен;Вже недоступний\r\n", new UTF8Encoding(true));
        WriteBook(Path.Combine(dir, "onec.xlsx"), "001", "004");
        WriteBook(Path.Combine(dir, "supplier.xlsx"), "002");
        var samples = Environment.GetEnvironmentVariable("SKU_SAMPLE_DIR");
        if (samples != null)
        {
            Directory.CreateDirectory(samples);
            foreach (var name in new[] { "site.csv", "onec.xlsx", "supplier.xlsx" }) File.Copy(Path.Combine(dir, name), Path.Combine(samples, name), true);
        }
    }
    private static void WriteBook(string path, params string[] skus)
    {
        using var book = new XSSFWorkbook();
        var sheet = book.CreateSheet("Товари");
        for (var i = 0; i < skus.Length; i++) sheet.CreateRow(i).CreateCell(0).SetCellValue(skus[i]);
        using var stream = File.Create(path);
        book.Write(stream, true);
    }
    private static void Render(Window window, string name)
    {
        var target = Environment.GetEnvironmentVariable("SKU_SCREENSHOT_DIR");
        if (target == null) return;
        Directory.CreateDirectory(target);
        window.UpdateLayout();
        var element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(target, name + ".png"));
        png.Save(stream);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void OnSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try { await action(); }
                catch (Exception ex) { failure = ex; }
                finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF smoke test timed out.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
