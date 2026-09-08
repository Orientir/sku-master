using System.IO;
using System.Text.Json;
using SkuMaster.Core;
using SkuMaster.Infrastructure;

namespace SkuMaster.Desktop;

public sealed record Choice(string Value, string Label);

public sealed class MainViewModel : ObservableObject
{
    private readonly FileService files = new();
    private readonly SettingsStore settingsStore;
    private readonly WorkflowState state = new();
    private CancellationTokenSource? cancellation;
    private string sitePath = "", oneCPath = "", supplierPath = "";
    private string message = "Додайте три файли, щоб перевірити наявність товарів.";
    private bool saving;
    private string? checkedSignature;
    private int filter;
    private IReadOnlyList<string> warnings = [];
    private string[] oneCSheets = [], supplierSheets = [], siteColumns = [];

    public MainViewModel(SettingsStore? store = null)
    {
        settingsStore = store ?? new SettingsStore();
        var loaded = settingsStore.Load();
        Settings = loaded.Settings;
        if (loaded.Warning != null) Message = loaded.Warning;
    }
    public AppSettings Settings { get; }
    public IReadOnlyList<ITableOperation> Operations { get; } = [new StatusOperation()];
    public string SitePath { get => sitePath; set { if (Set(ref sitePath, value)) Invalidate(); } }
    public string OneCPath { get => oneCPath; set { if (Set(ref oneCPath, value)) Invalidate(); } }
    public string SupplierPath { get => supplierPath; set { if (Set(ref supplierPath, value)) Invalidate(); } }
    public string[] OneCSheets { get => oneCSheets; private set => Set(ref oneCSheets, value); }
    public string[] SupplierSheets { get => supplierSheets; private set => Set(ref supplierSheets, value); }
    public string[] SiteColumns { get => siteColumns; private set => Set(ref siteColumns, value); }
    public string Message { get => message; set => Set(ref message, value); }
    public bool IsBusy => state.IsBusy || saving;
    public bool CanEdit => !IsBusy;
    public bool CanAnalyze => !IsBusy && new[] { SitePath, OneCPath, SupplierPath }.All(x => !string.IsNullOrWhiteSpace(x));
    public bool HasResult => state.Result != null;
    public bool CanSave => state.CanExport && !saving;
    public bool UnsavedResult => state.UnsavedResult;
    public OperationResult? Summary => state.Result;
    public IReadOnlyList<string> Warnings => warnings;
    public int WarningCount => warnings.Count;
    public int Filter { get => filter; set { if (Set(ref filter, value)) Notify(nameof(Changes)); } }
    public IEnumerable<StatusChange> Changes => state.Result?.Changes.Where(x => Filter == 0 ||
        (Filter == 1 && x.Kind == ChangeKind.Unavailable) || (Filter == 2 && x.Kind == ChangeKind.Available)) ?? [];
    public static Choice[] InputDelimiters { get; } = [new("auto", "Визначити автоматично"), new(";", "Крапка з комою (;)"), new(",", "Кома (,)"), new("\t", "Табуляція")];
    public static Choice[] InputEncodings { get; } = [new("auto", "Визначити автоматично"), new("utf-8", "UTF-8"), new("utf-8-bom", "UTF-8 з BOM"), new("windows-1251", "Windows-1251")];
    public static Choice[] OutputDelimiters { get; } = [new("source", "Як у файлі сайту"), .. InputDelimiters.Skip(1)];
    public static Choice[] OutputEncodings { get; } = [new("source", "Як у файлі сайту"), .. InputEncodings.Skip(1)];
    public static Choice[] Formats { get; } = [new("csv", "CSV"), new("xlsx", "Excel (.xlsx)")];

    public void Invalidate()
    {
        if (IsBusy) return;
        var hadResult = HasResult;
        state.Invalidate();
        checkedSignature = null;
        warnings = [];
        if (hadResult) Message = "Файли або налаштування змінено. Запустіть перевірку повторно.";
        Refresh();
    }

    public async Task LoadMetadataAsync(string source)
    {
        saving = true;
        Refresh();
        try
        {
            if (source == "site")
                SiteColumns = await Task.Run(() => files.ReadSite(SitePath, Settings.CsvInput).Headers);
            else if (source == "onec")
            {
                OneCSheets = await Task.Run(() => files.GetSheets(OneCPath));
                if (!OneCSheets.Contains(Settings.OneC.SheetName)) Settings.OneC.SheetName = OneCSheets.FirstOrDefault() ?? "";
            }
            else
            {
                SupplierSheets = await Task.Run(() => files.GetSheets(SupplierPath));
                if (!SupplierSheets.Contains(Settings.Supplier.SheetName)) Settings.Supplier.SheetName = SupplierSheets.FirstOrDefault() ?? "";
            }
            Notify(nameof(Settings));
            Message = "Файл додано. Перевірте налаштування та натисніть «Перевірити файли».";
        }
        catch (Exception ex) { Message = DescribeError(ex); }
        finally { saving = false; Refresh(); }
    }

    public async Task AnalyzeAsync()
    {
        if (!CanAnalyze) return;
        state.Begin();
        warnings = [];
        cancellation = new();
        Refresh();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Settings))!;
            var paths = new[] { SitePath, OneCPath, SupplierPath };
            var signature = Signature();
            var operation = Operations.SingleOrDefault(x => x.Id == settings.OperationId)
                ?? throw new ArgumentException("Оберіть доступну операцію.");
            var progress = new Progress<string>(value => Message = value);
            var token = cancellation.Token;
            var result = await Task.Run(() =>
            {
                ((IProgress<string>)progress).Report("Читаємо експорт сайту…");
                var table = files.ReadSite(paths[0], settings.CsvInput, token);
                ((IProgress<string>)progress).Report("Читаємо експорт із 1С…");
                var oneC = files.ReadSource(paths[1], settings.OneC, token);
                ((IProgress<string>)progress).Report("Читаємо файл постачальника…");
                var supplier = files.ReadSource(paths[2], settings.Supplier, token);
                var available = new HashSet<string>(oneC.Skus, StringComparer.Ordinal);
                available.UnionWith(supplier.Skus);
                ((IProgress<string>)progress).Report("Порівнюємо SKU та готуємо зміни…");
                var transformed = operation.Execute(table, available, settings.Rules, token);
                return transformed with { Warnings = oneC.Warnings.Concat(supplier.Warnings).Concat(transformed.Warnings).ToArray() };
            }, token);
            token.ThrowIfCancellationRequested();
            if (signature != Signature()) throw new IOException("Вхідний файл змінився під час перевірки. Запустіть перевірку повторно.");
            SiteColumns = result.Output.Headers;
            state.Complete(result);
            checkedSignature = signature;
            warnings = result.Warnings;
            Message = result.Changed == 0 ? "Перевірку завершено. Статуси не потребують змін. Результат можна зберегти."
                : $"Перевірку завершено. Зміниться статус у {result.Changed:N0} рядках. Перегляньте результат перед збереженням.";
            PersistSettings();
        }
        catch (OperationCanceledException) { state.Invalidate(); Message = "Перевірку скасовано. Файли не змінено."; }
        catch (Exception ex) { state.Invalidate(); Message = DescribeError(ex); }
        finally { cancellation.Dispose(); cancellation = null; Refresh(); }
    }

    public async Task SaveAsync(string path)
    {
        if (!CanSave) return;
        if (checkedSignature != Signature()) { Invalidate(); Message = "Вхідні файли або налаштування змінилися. Повторіть перевірку."; return; }
        saving = true;
        cancellation = new();
        Refresh();
        try
        {
            var output = state.Result!.Output;
            var export = JsonSerializer.Deserialize<ExportSettings>(JsonSerializer.Serialize(Settings.Export))!;
            export.SkuColumn = Settings.Rules.SkuColumn;
            export.StatusColumn = Settings.Rules.StatusColumn;
            var paths = new[] { SitePath, OneCPath, SupplierPath };
            Message = "Зберігаємо результат…";
            await Task.Run(() => files.Export(output, path, export, paths, cancellation.Token));
            state.MarkSaved();
            Message = $"Готово! Файл збережено: {path}";
            PersistSettings();
        }
        catch (OperationCanceledException) { Message = "Збереження скасовано."; }
        catch (Exception ex) { Message = DescribeError(ex); }
        finally { cancellation.Dispose(); cancellation = null; saving = false; Refresh(); }
    }

    public void Cancel() => cancellation?.Cancel();
    public void PersistSettings()
    {
        try { settingsStore.Save(Settings); }
        catch (Exception ex) { Message += $" Налаштування не збережено: {DescribeError(ex)}"; }
    }
    private string Signature() => JsonSerializer.Serialize(Settings) + string.Join("|", new[] { SitePath, OneCPath, SupplierPath }.Select(path =>
    {
        var info = new FileInfo(path);
        return info.Exists ? $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : $"{path}:missing";
    }));
    public static string DescribeError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Немає доступу до файлу або папки. Оберіть інше місце збереження або перевірте дозволи.",
        FileNotFoundException => "Файл не знайдено. Оберіть його повторно.",
        _ => ex.Message
    };
    private void Refresh()
    {
        foreach (var name in new[] { nameof(IsBusy), nameof(CanEdit), nameof(CanAnalyze), nameof(HasResult), nameof(CanSave), nameof(UnsavedResult), nameof(Summary), nameof(Changes), nameof(Warnings), nameof(WarningCount) }) Notify(name);
    }
}
