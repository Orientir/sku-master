using System.IO;
using System.Text.Json;
using SkuMaster.Core;
using SkuMaster.Core.Availability;
using SkuMaster.Core.MissingProducts;
using SkuMaster.Infrastructure;
using SkuMaster.Infrastructure.MissingProducts;

namespace SkuMaster.Desktop.Availability;

public sealed class AvailabilitySettings
{
    public CsvInputSettings Site { get; set; } = new();
    public int SkuColumn { get; set; } = 1;
    public int StatusColumn { get; set; } = 2;
    public MissingFileOptions Supplier { get; set; } = new();
    public ExportSettings Export { get; set; } = new() { Delimiter = ";", EncodingName = "utf-8-bom" };
}

public sealed class AvailabilityViewModel : ObservableObject
{
    private readonly string settingsPath;
    private CancellationTokenSource? cancellation;
    private string sitePath = "", supplierPath = "";
    private bool dirty;
    public AvailabilityViewModel(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkuMaster", "availability", "settings.json");
        try
        {
            if (File.Exists(this.settingsPath)) { var loaded = JsonSerializer.Deserialize<AvailabilitySettings>(File.ReadAllText(this.settingsPath))!; Validate(loaded); Settings = loaded; }
        }
        catch { Message = "Не вдалося прочитати налаштування. Використано стандартні значення."; }
    }
    public AvailabilitySettings Settings { get; private set; } = new();
    public string SitePath { get => sitePath; set { sitePath = value; Invalidate(); } }
    public string SupplierPath { get => supplierPath; set { supplierPath = value; Invalidate(); } }
    public IReadOnlyList<AvailabilityRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    public bool IsBusy { get; private set; }
    public bool HasResult { get; private set; }
    public bool UnsavedResult => dirty;
    public bool CanSave => HasResult && !IsBusy;
    public string Message { get; private set; } = "Додайте експорт сайту та залишки постачальника.";
    public void Invalidate() { Rows = []; Warnings = []; HasResult = false; dirty = false; Notify(""); }
    public void Cancel() => cancellation?.Cancel();
    public async Task AnalyzeAsync()
    {
        Invalidate();
        await Run(async token =>
        {
            var result = await Task.Run(() =>
            {
                var site = new FileService().ReadSite(SitePath, Settings.Site, token);
                var supplier = new MissingProductsFileService().ReadExclusions(SupplierPath, Settings.Supplier, token);
                return AvailabilityCheck.Execute(site, supplier, Settings.SkuColumn, Settings.StatusColumn, token);
            }, token);
            Rows = result.Rows; Warnings = result.Warnings; HasResult = true;
            Message = $"Перевірено {Rows.Count:N0} товарів. Оберіть статуси та перегляньте наявність.";
        });
    }
    public void Edit(int[] ids, string value)
    {
        if (!HasResult || IsBusy) throw new InvalidOperationException("Спочатку перевірте файли.");
        var edited = AvailabilityCheck.Edit(Rows, ids, value);
        if (!edited.SequenceEqual(Rows)) dirty = edited.Any(r => r.Changed);
        Rows = edited; Notify("");
    }
    public void ApplySettings(AvailabilitySettings next)
    {
        Validate(next);
        var inputChanged = JsonSerializer.Serialize(new { Settings.Site, Settings.Supplier, Settings.SkuColumn, Settings.StatusColumn }) != JsonSerializer.Serialize(new { next.Site, next.Supplier, next.SkuColumn, next.StatusColumn });
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        FileService.AtomicWrite(settingsPath, stream => JsonSerializer.Serialize(stream, next));
        Settings = next;
        if (inputChanged) Invalidate();
        Notify("");
    }
    private static void Validate(AvailabilitySettings next)
    {
        if (next.Site is null || next.Supplier is null || next.Export is null || next.SkuColumn < 1 || next.StatusColumn < 1 || next.SkuColumn == next.StatusColumn || next.Supplier.SkuColumn < 1 || next.Supplier.FirstDataRow < 1)
            throw new ArgumentException("Перевірте номери колонок та першого рядка даних.");
        if (next.Export.Format is not ("csv" or "xlsx")) throw new ArgumentException("Оберіть CSV або XLSX.");
    }
    public async Task SaveAsync(string path, int[] ids, bool report)
    {
        if (!CanSave) throw new InvalidOperationException("Спочатку перевірте файли.");
        var scope = ids.ToHashSet();
        if (scope.Except(Rows.Select(r => r.Row)).Any()) throw new ArgumentException("Невідомі рядки результату.");
        var rows = Rows.Where(r => scope.Contains(r.Row)).ToArray();
        var table = report ? new TableData(["sku", "status", "Наявність"], rows.Select(r => new[] { r.Sku, r.Current, r.Available ? "Є у постачальника" : "Немає у постачальника" }).ToArray()) : AvailabilityCheck.Export(rows, Settings.Export.OnlyChanged);
        if (table.Rows.Count == 0) throw new InvalidOperationException("Немає товарів для збереження за вибраними умовами.");
        await Run(async token =>
        {
            await Task.Run(() => new FileService().Export(table, path, Settings.Export, [SitePath, SupplierPath], token), token);
            if (!report && Rows.Where(r => r.Changed).All(r => scope.Contains(r.Row))) dirty = false;
            Message = $"Збережено {table.Rows.Count:N0} товарів.";
        });
    }
    private async Task Run(Func<CancellationToken, Task> work)
    {
        if (IsBusy) throw new InvalidOperationException("Дочекайтеся завершення операції.");
        IsBusy = true; cancellation = new(); Notify("");
        try { await work(cancellation.Token); }
        catch (OperationCanceledException) { Message = "Операцію скасовано."; throw new InvalidOperationException(Message); }
        catch (Exception ex) { Message = ex.Message; throw; }
        finally { IsBusy = false; cancellation.Dispose(); cancellation = null; Notify(""); }
    }
}
