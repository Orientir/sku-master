using System.IO;
using System.Text.Json;
using SkuMaster.Core;
using SkuMaster.Infrastructure;

namespace SkuMaster.Desktop;

public sealed record Choice(string Value, string Label);
public sealed record StatusFilterOption(string? Value, string Label);

public sealed class MainViewModel : ObservableObject
{
    private readonly FileService files = new();
    private readonly SettingsStore settingsStore;
    private readonly WorkflowState state = new();
    private ResultReview? review;
    private CancellationTokenSource? cancellation;
    private string sitePath = "", oneCPath = "", supplierPath = "";
    private string message = "Додайте три файли, щоб перевірити наявність товарів.";
    private bool saving;
    private string? checkedSignature;
    private int filter;
    private string searchText = "";
    private static readonly StatusFilterOption AllOldStatuses = new(null, "Усі старі статуси");
    private static readonly StatusFilterOption AllNewStatuses = new(null, "Усі нові статуси");
    private StatusFilterOption selectedOldStatus = AllOldStatuses, selectedNewStatus = AllNewStatuses;
    private IReadOnlyList<StatusChange> filteredChanges = [];
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
    public bool ExportOnlyChanged
    {
        get => Settings.Export.OnlyChanged;
        set { if (Settings.Export.OnlyChanged != value) { Settings.Export.OnlyChanged = value; Notify(); } }
    }
    public OperationResult? Summary => state.Result;
    public IReadOnlyList<string> Warnings => warnings;
    public int WarningCount => warnings.Count;
    public int Filter { get => filter; set { if (Set(ref filter, value)) RefreshFilters(); } }
    public string SearchText { get => searchText; set { if (Set(ref searchText, value ?? "")) RefreshFilters(); } }
    public StatusFilterOption SelectedOldStatus { get => selectedOldStatus; set { if (Set(ref selectedOldStatus, value ?? AllOldStatuses)) RefreshFilters(); } }
    public StatusFilterOption SelectedNewStatus { get => selectedNewStatus; set { if (Set(ref selectedNewStatus, value ?? AllNewStatuses)) RefreshFilters(); } }
    public IReadOnlyList<StatusFilterOption> OldStatusOptions { get; private set; } = [AllOldStatuses];
    public IReadOnlyList<StatusFilterOption> NewStatusOptions { get; private set; } = [AllNewStatuses];
    public IReadOnlyList<StatusChange> Changes => filteredChanges;
    public IReadOnlyList<StatusChange> AllRows => review?.Rows ?? [];
    public string FilterSummary => $"Показано {filteredChanges.Count:N0} із {state.Result?.Total ?? 0:N0} рядків · {FilterLabel}";
    public string FilterLabel => Filter switch { 1 => "Стали недоступними", 2 => "Знову доступні", 3 => "Перевірено", 4 => "Без змін", _ => "Усі зміни" };
    public bool NoMatches => HasResult && filteredChanges.Count == 0;
    public string EmptyResultMessage => "За цими умовами рядків не знайдено. Оберіть інший блок або скиньте фільтри.";
    public void ResetFilters()
    {
        searchText = ""; filter = 0; selectedOldStatus = AllOldStatuses; selectedNewStatus = AllNewStatuses;
        foreach (var property in new[] { nameof(SearchText), nameof(Filter), nameof(SelectedOldStatus), nameof(SelectedNewStatus) }) Notify(property);
        RefreshFilters();
    }
    public void SelectSummary(int kind)
    {
        ResetFilters();
        Filter = kind;
    }
    public void EditStatus(int rowNumber, string status)
    {
        if (IsBusy || !HasResult || review is null || !review.SetStatus(rowNumber, status)) return;
        state.Complete(review.Result);
        PrepareFilterOptions(false);
        Message = $"Статус у рядку {rowNumber} змінено. Усього змін: {review.Result.Changed:N0}. Збережіть результат.";
        Refresh();
    }
    private void RefreshFilters()
    {
        filteredChanges = ChangeQuery.Apply(HasResult ? review?.Rows ?? [] : [], SearchText, Filter, SelectedOldStatus.Value, SelectedNewStatus.Value);
        foreach (var property in new[] { nameof(Changes), nameof(FilterSummary), nameof(FilterLabel), nameof(NoMatches), nameof(EmptyResultMessage) }) Notify(property);
    }
    private void PrepareFilterOptions(bool reset = true)
    {
        static StatusFilterOption[] Options(IEnumerable<string> values, StatusFilterOption all) =>
            [all, .. values.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => new StatusFilterOption(x, x.Length == 0 ? "(порожньо)" : x))];
        OldStatusOptions = Options(review?.Rows.Select(x => x.OldStatus) ?? [], AllOldStatuses);
        NewStatusOptions = Options(review?.Rows.Select(x => x.NewStatus) ?? [], AllNewStatuses);
        var old = selectedOldStatus; var next = selectedNewStatus;
        Notify(nameof(OldStatusOptions)); Notify(nameof(NewStatusOptions));
        if (reset) ResetFilters();
        else
        {
            SelectedOldStatus = OldStatusOptions.FirstOrDefault(x => x.Value == old.Value) ?? AllOldStatuses;
            SelectedNewStatus = NewStatusOptions.FirstOrDefault(x => x.Value == next.Value) ?? AllNewStatuses;
            Notify(nameof(SelectedOldStatus)); Notify(nameof(SelectedNewStatus));
        }
    }
    public static Choice[] InputDelimiters { get; } = [new("auto", "Визначити автоматично"), new(";", "Крапка з комою (;)"), new(",", "Кома (,)"), new("\t", "Табуляція")];
    public static Choice[] InputEncodings { get; } = [new("auto", "Визначити автоматично"), new("utf-8", "UTF-8"), new("utf-8-bom", "UTF-8 з BOM"), new("windows-1251", "Windows-1251")];
    public static Choice[] OutputDelimiters { get; } = [new("source", "Як у файлі сайту"), .. InputDelimiters.Skip(1)];
    public static Choice[] OutputEncodings { get; } = [new("source", "Як у файлі сайту"), .. InputEncodings.Skip(1)];
    public static Choice[] Formats { get; } = [new("csv", "CSV"), new("xlsx", "Excel (.xlsx)")];
    public static IReadOnlyList<Choice> NonEmptyStatusChoices { get; } = StatusCatalog.Values.Select(x => new Choice(x, x)).ToArray();
    public static IReadOnlyList<Choice> StatusChoices { get; } = new[] { new Choice("", "Без статусу") }.Concat(NonEmptyStatusChoices).ToArray();

    public void Invalidate()
    {
        if (IsBusy) return;
        var hadResult = HasResult;
        state.Invalidate();
        review = null;
        PrepareFilterOptions();
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
        review = null;
        warnings = [];
        cancellation = new();
        Refresh();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Settings))!;
            if (!settings.CsvInput.HasHeader)
            {
                settings.Rules.SkuColumn = "sku";
                settings.Rules.StatusColumn = "status";
            }
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
                return transformed with
                {
                    Warnings = oneC.Warnings.Concat(supplier.Warnings).Concat(transformed.Warnings).ToArray(),
                    SourcesBySku = available.ToDictionary(sku => sku, sku =>
                        (oneC.Skus.Contains(sku) ? AvailabilitySource.OneC : AvailabilitySource.None)
                        | (supplier.Skus.Contains(sku) ? AvailabilitySource.Supplier : AvailabilitySource.None), StringComparer.Ordinal)
                };
            }, token);
            token.ThrowIfCancellationRequested();
            if (signature != Signature()) throw new IOException("Вхідний файл змінився під час перевірки. Запустіть перевірку повторно.");
            SiteColumns = result.Output.Headers;
            review = new ResultReview(result, settings.Rules.SkuColumn, settings.Rules.StatusColumn, settings.Rules.UnavailableStatus);
            state.Complete(review.Result);
            PrepareFilterOptions();
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

    public bool LastExportSucceeded { get; private set; }
    public async Task SaveAsync(string path)
    {
        LastExportSucceeded = false;
        if (!CanSave) return;
        if (checkedSignature != Signature()) { Invalidate(); Message = "Вхідні файли або налаштування змінилися. Повторіть перевірку."; return; }
        saving = true;
        cancellation = new();
        Refresh();
        try
        {
            var export = JsonSerializer.Deserialize<ExportSettings>(JsonSerializer.Serialize(Settings.Export))!;
            var output = state.Result!.GetExportTable(export.OnlyChanged);
            export.SkuColumn = Settings.CsvInput.HasHeader ? Settings.Rules.SkuColumn : "sku";
            export.StatusColumn = Settings.CsvInput.HasHeader ? Settings.Rules.StatusColumn : "status";
            var paths = new[] { SitePath, OneCPath, SupplierPath };
            Message = "Зберігаємо результат…";
            await Task.Run(() => files.Export(output, path, export, paths, cancellation.Token));
            state.MarkSaved();
            LastExportSucceeded = true;
            Message = $"Готово! Збережено {output.Rows.Count:N0} рядків: {path}";
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
    private string Signature()
    {
        var settings = JsonSerializer.SerializeToNode(Settings)!;
        // Export scope selects already processed rows; it does not change their values.
        settings.AsObject().Remove(nameof(AppSettings.Export));
        return settings.ToJsonString() + string.Join("|", new[] { SitePath, OneCPath, SupplierPath }.Select(path =>
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : $"{path}:missing";
        }));
    }
    public static string DescribeError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Немає доступу до файлу або папки. Оберіть інше місце збереження або перевірте дозволи.",
        FileNotFoundException => "Файл не знайдено. Оберіть його повторно.",
        _ => ex.Message
    };
    private void Refresh()
    {
        RefreshFilters();
        foreach (var name in new[] { nameof(IsBusy), nameof(CanEdit), nameof(CanAnalyze), nameof(HasResult), nameof(CanSave), nameof(UnsavedResult), nameof(Summary), nameof(Changes), nameof(Warnings), nameof(WarningCount) }) Notify(name);
    }
}
