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
            var output = Path.Combine(dir, "result.csv");
            await model.SaveAsync(output);
            Assert.True(File.Exists(output), model.Message);
            Assert.False(model.UnsavedResult);
            var result = new FileService().ReadSite(output, new());
            Assert.Equal("", result.Rows[0][1]);
            Assert.Equal("", result.Rows[1][1]);
            Assert.Equal("Временно недоступен", result.Rows[2][1]);
            Assert.Equal("Акція", result.Rows[3][1]);
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
            window = new MainWindow(model) { Left = -10000, Top = -10000, ShowActivated = false };
            // Use the window's actual view model so event handlers and bindings share state.
            var actual = (MainViewModel)window.DataContext;
            actual.SitePath = model.SitePath;
            actual.OneCPath = model.OneCPath;
            actual.SupplierPath = model.SupplierPath;
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var tabs = (TabControl)window.FindName("Tabs");
            Render(window, "01-files");
            tabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await actual.LoadMetadataAsync("site");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var skuCombo = Descendants<ComboBox>(window).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Колонка SKU");
            var statusCombo = Descendants<ComboBox>(window).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Колонка статусу");
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
            Assert.True(actual.CanSave, actual.Message);
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
        File.WriteAllText(Path.Combine(dir, "site.csv"), "sku;status;назва\r\n001;Временно недоступен;Товар із 1С\r\n002;Временно недоступен;Товар постачальника\r\n003;;Відсутній товар\r\n004;Акція;Акційний товар\r\n005;Временно недоступен;Вже недоступний\r\n", new UTF8Encoding(true));
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
