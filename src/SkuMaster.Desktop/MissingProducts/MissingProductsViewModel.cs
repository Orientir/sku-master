using System.IO;
using System.Text.Json;
using SkuMaster.Core.MissingProducts;
using SkuMaster.Infrastructure.MissingProducts;

namespace SkuMaster.Desktop.MissingProducts;

public sealed record MissingProductRow(string Sku, string Name, bool IsExcluded);

public sealed class MissingProductsViewModel : ObservableObject
{
    private readonly MissingProductsFileService files = new();
    private readonly ExclusionStore exclusionsStore;
    private readonly MissingModuleSettingsStore settingsStore;
    private IReadOnlyList<MissingProduct> supplier = [];
    private IReadOnlyList<string> site = [], exclusions = [];
    private MissingSearchResult? result;
    private CancellationTokenSource? cancellation;
    private string sitePath = "", supplierPath = "", exclusionPath = "", search = "", exclusionSearch = "";
    private string message = "Додайте файл сайту й постачальника. Збережені виключення застосуються автоматично.";
    private string? checkedSignature;
    private bool busy, dirty, exclusionsReady = true, inputsValid = true;
    private int filter = 3;
    private HashSet<string> visibleScope = new(StringComparer.Ordinal);
    public int Filter => filter;
    public object[] AllReviewRows
    {
        get
        {
            var excluded = exclusions.ToHashSet(StringComparer.Ordinal);
            var onSite = site.Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
            return supplier.Where(x => !string.IsNullOrWhiteSpace(x.Sku)).DistinctBy(x => x.Sku.Trim(), StringComparer.Ordinal)
                .Select(x => (object)new { sku = x.Sku.Trim(), name = x.Name, excluded = excluded.Contains(x.Sku.Trim()), onSite = onSite.Contains(x.Sku.Trim()) }).ToArray();
        }
    }
    public IReadOnlyList<string> SavedExclusions => exclusions;
    public IReadOnlyList<MissingProductRow> Rows
    {
        get
        {
            var excluded = exclusions.ToHashSet(StringComparer.Ordinal);
            return supplier.Where(x => !string.IsNullOrWhiteSpace(x.Sku)).DistinctBy(x => x.Sku.Trim(), StringComparer.Ordinal)
                .Where(x => visibleScope.Contains(x.Sku.Trim()) && (Matches(x.Sku, Search) || Matches(x.Name, Search)))
                .Select(x => new MissingProductRow(x.Sku.Trim(), x.Name, excluded.Contains(x.Sku.Trim()))).ToArray();
        }
    }
    public void SelectSummary(int value)
    {
        filter = value; search = "";
        var onSite = site.Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
        var excluded = exclusions.ToHashSet(StringComparer.Ordinal);
        visibleScope = supplier.Select(x => x.Sku.Trim()).Where(x => x.Length > 0 && (value switch
        {
            1 => onSite.Contains(x),
            2 => !onSite.Contains(x) && excluded.Contains(x),
            3 => !onSite.Contains(x) && !excluded.Contains(x),
            _ => true
        })).ToHashSet(StringComparer.Ordinal);
        Notify(nameof(Filter)); Notify(nameof(Search)); Refresh();
    }
    public void SetExcluded(string sku, bool value)
    {
        if (busy || result is null || !exclusionsReady || !supplier.Any(x => x.Sku.Trim() == sku)) return;
        ReplaceExclusions(value ? exclusions.Append(sku) : exclusions.Where(x => x != sku),
            value ? $"Артикул {sku} додано до виключень. Щоб скасувати, зніміть галочку." : $"Артикул {sku} прибрано з виключень.");
        Refresh();
    }

    public MissingProductsViewModel(string? exclusionStorePath = null, string? settingsPath = null)
    {
        exclusionsStore = new(exclusionStorePath);
        settingsStore = new(settingsPath);
        try { Settings = settingsStore.Load(); }
        catch (Exception ex) { Settings = new(); message = ex.Message; }
        try { exclusions = exclusionsStore.Load(); }
        catch (Exception ex) { exclusionsReady = false; message = $"{ex.Message} Імпортуйте виключення повторно перед пошуком."; }
    }
    public MissingModuleSettings Settings { get; }
    public string SitePath { get => sitePath; set { if (Set(ref sitePath, value)) Invalidate(); } }
    public string SupplierPath { get => supplierPath; set { if (Set(ref supplierPath, value)) Invalidate(); } }
    public string ExclusionPath { get => exclusionPath; set => Set(ref exclusionPath, value); }
    public string Message { get => message; set => Set(ref message, value); }
    public bool IsBusy => busy;
    public bool CanEdit => !busy;
    public bool InputsValid { get => inputsValid; set { inputsValid = value; Refresh(); } }
    public bool CanAnalyze => !busy && inputsValid && exclusionsReady && !string.IsNullOrWhiteSpace(SitePath) && !string.IsNullOrWhiteSpace(SupplierPath);
    public bool CanExport => !busy && result is not null;
    public bool CanExportExclusions => !busy && exclusionsReady;
    public bool HasResult => result is not null;
    public bool UnsavedResult => dirty;
    public MissingSearchResult? Summary => result;
    public string Search { get => search; set { if (Set(ref search, value)) Refresh(); } }
    public string ExclusionSearch { get => exclusionSearch; set { if (Set(ref exclusionSearch, value)) Refresh(); } }
    public IReadOnlyList<MissingProduct> Products => (result?.Products ?? []).Where(x => Matches(x.Sku, Search) || Matches(x.Name, Search)).ToArray();
    public IReadOnlyList<string> Exclusions => exclusions.Where(x => Matches(x, ExclusionSearch)).ToArray();
    public int ExclusionCount => exclusions.Count;
    public string ResultCount => $"Показано {Rows.Count:N0} · До експорту: {result?.Products.Count ?? 0:N0}. Галочки зберігаються одразу; рядки залишаються до зміни фільтра.";
    public IReadOnlyList<string> Warnings => result?.Warnings ?? [];
    public static Choice[] Formats { get; } = [new("csv", "CSV"), new("xlsx", "Excel (.xlsx)")];
    public static Choice[] Delimiters { get; } = [new("auto", "Автоматично"), new(";", "Крапка з комою"), new(",", "Кома"), new("\t", "Табуляція")];
    public static Choice[] Encodings { get; } = [new("auto", "Автоматично"), new("utf-8", "UTF-8"), new("windows-1251", "Windows-1251")];
    private static string DescribeError(Exception ex) => ex is UnauthorizedAccessException ? "Немає доступу до файлу або папки. Перевірте дозволи." : ex.Message;
    private static bool Matches(string text, string query) => text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Invalidate()
    {
        if (busy) return;
        if (result is not null) Message = "Файл або налаштування змінено. Запустіть пошук повторно.";
        result = null; checkedSignature = null; dirty = false; supplier = []; site = []; visibleScope.Clear();
        Refresh();
    }
    private string Signature() => JsonSerializer.Serialize(new { Settings.Site, Settings.Supplier }) + string.Join("|", new[] { SitePath, SupplierPath }.Select(path =>
    {
        var info = new FileInfo(path);
        return info.Exists ? $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : path;
    }));
    public async Task AnalyzeAsync()
    {
        if (!CanAnalyze) return;
        Invalidate(); busy = true; cancellation = new(); Refresh();
        try
        {
            var signature = Signature();
            var settings = JsonSerializer.Deserialize<MissingModuleSettings>(JsonSerializer.Serialize(Settings))!;
            var paths = new[] { SitePath, SupplierPath };
            Message = "Читаємо файли та шукаємо товари, яких немає на сайті…";
            var token = cancellation.Token;
            var loaded = await Task.Run(() =>
            {
                var siteRows = files.ReadSite(paths[0], settings.Site, token);
                var supplierRows = files.ReadSupplier(paths[1], settings.Supplier, token);
                var searchResult = MissingProductsSearch.Execute(supplierRows, siteRows, exclusions, token);
                return (siteRows, supplierRows, searchResult);
            }, token);
            token.ThrowIfCancellationRequested();
            if (signature != Signature()) throw new IOException("Вхідний файл змінився під час пошуку. Повторіть перевірку.");
            (site, supplier, result) = loaded;
            checkedSignature = signature; dirty = true;
            SelectSummary(3);
            Message = $"Знайдено {result.Products.Count:N0} товарів, яких немає на сайті та у виключеннях.";
            PersistSettings();
        }
        catch (OperationCanceledException) { Message = "Пошук скасовано."; }
        catch (Exception ex) { Message = DescribeError(ex); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; Refresh(); }
    }
    public bool LastImportSucceeded { get; private set; }
    public async Task ImportExclusionsAsync(string path)
    {
        LastImportSucceeded = false;
        if (busy || !inputsValid) return;
        busy = true; cancellation = new(); Refresh();
        try
        {
            var options = JsonSerializer.Deserialize<MissingFileOptions>(JsonSerializer.Serialize(Settings.Exclusions))!;
            var token = cancellation.Token;
            var imported = await Task.Run(() => files.ReadExclusions(path, options, token), token);
            token.ThrowIfCancellationRequested();
            exclusionsStore.Save(imported);
            exclusions = imported.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            exclusionsReady = true; ExclusionPath = path;
            LastImportSucceeded = true;
            Recalculate();
            Message = $"Виключення збережено: {exclusions.Count:N0} артикулів. Повторне завантаження при запуску не потрібне.";
            PersistSettings();
        }
        catch (OperationCanceledException) { Message = "Імпорт виключень скасовано. Попередній список збережено."; }
        catch (Exception ex) { Message = DescribeError(ex); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; Refresh(); }
    }
    public void AddExclusions(IEnumerable<MissingProduct> selected)
    {
        if (busy || result is null || !exclusionsReady) return;
        var available = result.Products.Select(x => x.Sku.Trim()).ToHashSet(StringComparer.Ordinal);
        var skus = selected.Select(x => x.Sku.Trim()).Where(available.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (skus.Length == 0) { Message = "Оберіть товари у результатах (Ctrl або Shift для кількох)."; return; }
        ReplaceExclusions(exclusions.Concat(skus), $"Додано до виключень: {skus.Length:N0}. Список результатів оновлено.");
    }
    public void RemoveExclusions(IEnumerable<string> selected)
    {
        if (busy || !exclusionsReady) return;
        var removed = selected.ToHashSet(StringComparer.Ordinal);
        if (removed.Count == 0) { Message = "Оберіть артикули, які потрібно прибрати з виключень."; return; }
        ReplaceExclusions(exclusions.Where(x => !removed.Contains(x)), "Артикули прибрано з виключень. Поточний результат оновлено.");
    }
    private void ReplaceExclusions(IEnumerable<string> values, string success)
    {
        try
        {
            var next = values.Distinct(StringComparer.Ordinal).ToArray();
            exclusionsStore.Save(next);
            exclusions = next; Recalculate(); Message = success; Refresh();
        }
        catch (Exception ex) { Message = DescribeError(ex); }
    }
    private void Recalculate()
    {
        if (result is null) return;
        result = MissingProductsSearch.Execute(supplier, site, exclusions);
        dirty = true;
    }
    public bool LastExportSucceeded { get; private set; }
    public async Task ExportAsync(string path)
    {
        LastExportSucceeded = false;
        if (!CanExport) return;
        try { if (checkedSignature != Signature()) { Invalidate(); return; } }
        catch (Exception ex) { Message = DescribeError(ex); return; }
        busy = true; cancellation = new(); Refresh();
        try
        {
            var output = result!.Products;
            var format = Settings.OutputFormat;
            var paths = new[] { SitePath, SupplierPath, ExclusionPath, exclusionsStore.FilePath, settingsStore.FilePath }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            await Task.Run(() => files.Export(output, path, format, paths, cancellation.Token));
            LastExportSucceeded = true;
            dirty = false; Message = $"Збережено {output.Count:N0} товарів: {path}"; PersistSettings();
        }
        catch (OperationCanceledException) { Message = "Збереження скасовано."; }
        catch (Exception ex) { Message = DescribeError(ex); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; Refresh(); }
    }
    public void Cancel() => cancellation?.Cancel();
    public async Task ExportExclusionsAsync(string path)
    {
        LastExportSucceeded = false;
        if (!CanExportExclusions) return;
        busy = true; cancellation = new(); Refresh();
        try
        {
            var output = exclusions.ToArray();
            var format = Settings.OutputFormat;
            var paths = new[] { SitePath, SupplierPath, ExclusionPath, exclusionsStore.FilePath, settingsStore.FilePath };
            await Task.Run(() => files.ExportExclusions(output, path, format, paths, cancellation.Token));
            LastExportSucceeded = true;
            Message = $"Збережено {output.Length:N0} виключень: {path}";
            PersistSettings();
        }
        catch (OperationCanceledException) { Message = "Збереження виключень скасовано."; }
        catch (Exception ex) { Message = DescribeError(ex); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; Refresh(); }
    }
    public void PersistSettings()
    {
        try { settingsStore.Save(Settings); }
        catch (Exception ex) { Message += $" Налаштування не збережено: {ex.Message}"; }
    }
    private void Refresh()
    {
        Notify(nameof(Rows));
        Notify(nameof(CanExportExclusions));
        foreach (var property in new[] { nameof(IsBusy), nameof(CanEdit), nameof(CanAnalyze), nameof(CanExport), nameof(HasResult), nameof(UnsavedResult), nameof(Summary), nameof(Products), nameof(Exclusions), nameof(ExclusionCount), nameof(ResultCount), nameof(Warnings) }) Notify(property);
    }
}
