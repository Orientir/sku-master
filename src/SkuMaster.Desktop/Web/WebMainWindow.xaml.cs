using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using SkuMaster.Desktop.MissingProducts;

namespace SkuMaster.Desktop.Web;

public partial class WebMainWindow : Window
{
    private const string Origin = "https://sku-master.local";
    private readonly DesktopBridge bridge;
    private readonly string? profileOverride;
    private readonly UpdateService updates = new();
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool ready, allowClose, checkingUpdates, updateReady, closed;
    public WebMainWindow() : this(new DesktopBridge(new MainViewModel(), new MissingProductsViewModel())) { }
    public WebMainWindow(DesktopBridge bridge, string? profileOverride = null)
    {
        this.bridge = bridge;
        this.profileOverride = profileOverride;
        InitializeComponent();
        Loaded += InitializeBrowser;
        Closing += OnClosing;
        Closed += (_, _) => { closed = true; ready = false; refresh.Stop(); Browser.Dispose(); };
        refresh.Tick += (_, _) => { refresh.Stop(); SendSnapshot(); };
        bridge.Status.PropertyChanged += Changed;
        bridge.Missing.PropertyChanged += Changed;
        bridge.Availability.PropertyChanged += Changed;
        bridge.Images.PropertyChanged += Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!ready) return;
        if (!refresh.IsEnabled) refresh.Start();
    }
    private async void InitializeBrowser(object sender, RoutedEventArgs e)
    {
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "WebUi");
            if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("Не знайдено файли інтерфейсу. Перевстановіть програму.");
            var profile = profileOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkuMaster", "webview");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("sku-master.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) => { if (!IsLocal(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!IsLocal(args.Request.Uri)) args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) ShowError("Не вдалося відкрити інтерфейс: " + args.WebErrorStatus);
            };
            core.ProcessFailed += (_, _) => ShowError("Інтерфейс завершив роботу. Закрийте й відкрийте програму знову.");
            core.Navigate(Origin + "/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("Для інтерфейсу потрібен Microsoft Edge WebView2 Runtime. Встановіть його та відкрийте програму знову.");
            if (MessageBox.Show(this, "Відкрити офіційну сторінку встановлення WebView2?", "SKU Майстер", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private static bool IsLocal(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "sku-master.local" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);
    private void ShowError(string message)
    {
        Browser.Visibility = Visibility.Collapsed;
        Loading.Text = message;
        Loading.TextWrapping = TextWrapping.Wrap;
        Loading.MaxWidth = 600;
        // Keep the native title bar available when initialization fails.
        WindowStyle = WindowStyle.SingleBorderWindow;
    }
    private void Post(object value)
    {
        if (!closed) Browser.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(value, DesktopBridge.Json));
    }
    private void SendSnapshot() { if (ready) Post(new { snapshot = bridge.Snapshot() }); }
    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsLocal(e.Source)) return;
        long id = 0;
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            id = root.GetProperty("id").GetInt64();
            var module = root.GetProperty("module").GetString()!;
            var action = root.GetProperty("action").GetString()!;
            var data = root.GetProperty("data").Clone();
            object? value = null;
            if (module == "app") value = await AppAction(action, data);
            else if(module=="images")
            {
                if(action=="pick")
                {
                    if(bridge.Images.IsBusy)throw new InvalidOperationException("Дочекайтеся завершення обробки.");
                    var dialog=new OpenFileDialog{Filter="Зображення (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp",Multiselect=true,CheckFileExists=true};
                    if(dialog.ShowDialog(this)==true)value=await bridge.Images.ExecuteAsync("loadFiles",JsonSerializer.SerializeToElement(new{paths=dialog.FileNames,options=data.GetProperty("options").Clone()}));
                    else value=new{cancelled=true};
                }
                else value=await bridge.Images.ExecuteAsync(action,data);
            }
            else if (action == "pick")
            {
                if (module == "availability" ? bridge.Availability.IsBusy : module == "status" ? bridge.Status.IsBusy : bridge.Missing.IsBusy) throw new InvalidOperationException("Дочекайтеся завершення операції.");
                var source = data.GetProperty("source").GetString()!;
                var dialog = new OpenFileDialog { Filter = source == "site" ? "CSV (*.csv)|*.csv" : "Excel (*.xls;*.xlsx)|*.xls;*.xlsx", CheckFileExists = true };
                if (dialog.ShowDialog(this) == true)
                {
                    await bridge.PickAsync(module, source, dialog.FileName);
                    if (module == "missing") value = new { rows = bridge.Missing.AllReviewRows };
                }
                else value = new { cancelled = true };
            }
            else
            {
                if (action is "save" or "saveExclusions" or "saveReport")
                {
                    var folder = data.GetProperty("folder").GetString() ?? "";
                    var name = data.GetProperty("fileName").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
                        throw new InvalidDataException("Вкажіть коректну назву файлу без шляху.");
                    if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) throw new DirectoryNotFoundException("Оберіть наявну папку для збереження.");
                    var format = module == "availability" ? bridge.Availability.Settings.Export.Format : module == "status" ? bridge.Status.Settings.Export.Format : bridge.Missing.Settings.OutputFormat;
                    var path = Path.Combine(folder, name.EndsWith("." + format, StringComparison.OrdinalIgnoreCase) ? name : name + "." + format);
                    if (File.Exists(path) && MessageBox.Show(this, $"Файл «{Path.GetFileName(path)}» уже існує. Замінити його?", "Збереження результату", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    { Post(new { id, value = new { cancelled = true } }); return; }
                    data = module == "availability" ? JsonSerializer.SerializeToElement(new { path, rows = data.GetProperty("rows").Clone() }) : JsonSerializer.SerializeToElement(new { path });
                }
                await bridge.ExecuteAsync(module, action, data);
                if (module == "missing" && action == "analyze") value = new { rows = bridge.Missing.AllReviewRows };
            }
            if (allowClose) return;
            refresh.Stop();
            if (module != "app" || action == "ready") SendSnapshot();
            Post(new { id, value });
        }
        catch (Exception ex) { SendSnapshot(); Post(new { id, error = ex.Message }); }
    }
    private async Task<object?> AppAction(string action, JsonElement data)
    {
        switch (action)
        {
            case "ready":
                ready = true; Loading.Visibility = Visibility.Collapsed;
                _ = CheckUpdatesAsync(false);
                break;
            case "copy": Clipboard.SetText(data.GetProperty("text").GetString() ?? ""); break;
            case "folder":
                var dialog = new OpenFolderDialog { Title = "Папка для збереження" };
                return dialog.ShowDialog(this) == true ? new { path = dialog.FolderName } : null;
            case "drag": if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); break;
            case "minimize": WindowState = WindowState.Minimized; break;
            case "maximize": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
            case "close": Close(); break;
            case "closeConfirmed": if (bridge.IsBusy) throw new InvalidOperationException("Дочекайтеся завершення операції."); allowClose = true; Close(); break;
            case "updates":
                await CheckUpdatesAsync(true);
                break;
            case "restart":
                if (bridge.IsBusy || bridge.Unsaved) throw new InvalidOperationException("Збережіть результат і завершіть обробку в усіх модулях перед перезапуском.");
                if (updateReady) { allowClose = true; updates.ApplyAndRestart(); }
                break;
            default: throw new InvalidDataException("Невідома дія програми.");
        }
        return null;
    }
    private async Task CheckUpdatesAsync(bool manual)
    {
        if (checkingUpdates) return;
        checkingUpdates = true;
        Post(new { @event = "updateBusy", busy = true });
        try
        {
            updateReady = await updates.CheckAndDownloadAsync(text =>
            {
                if (manual) Dispatcher.Invoke(() => Post(new { @event = "updateStatus", message = text }));
            });
            if (updateReady)
            {
                if (manual && !bridge.IsBusy && !bridge.Unsaved) Post(new { @event = "updateReady" });
                else Post(new { @event = "updateStatus", message = manual
                    ? "Збережіть результат і завершіть обробку в усіх модулях перед оновленням."
                    : "Оновлення завантажено. Натисніть «Оновлення», щоб встановити його." });
            }
        }
        catch (Exception)
        {
            if (manual) Post(new { @event = "updateStatus", message = "Не вдалося перевірити оновлення. Спробуйте пізніше." });
        }
        finally { checkingUpdates = false; Post(new { @event = "updateBusy", busy = false }); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (bridge.IsBusy)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Дочекайтеся завершення операції або скасуйте її.", "SKU Майстер", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (ready && bridge.Unsaved) { e.Cancel = true; Post(new { @event = "close" }); }
    }
}
