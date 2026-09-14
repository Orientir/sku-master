using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkuMaster.Core;
using SkuMaster.Core.MissingProducts;
using SkuMaster.Desktop.MissingProducts;

namespace SkuMaster.Desktop.Web;

// The local UI receives explicit DTOs, never filesystem or .NET object access.
public sealed class DesktopBridge(MainViewModel status, MissingProductsViewModel missing, SkuMaster.Desktop.Availability.AvailabilityViewModel? availability = null, SkuMaster.Desktop.Images.ImagesViewModel? images = null)
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public MainViewModel Status { get; } = status;
    public MissingProductsViewModel Missing { get; } = missing;
    public SkuMaster.Desktop.Availability.AvailabilityViewModel Availability { get; } = availability ?? new();
    public SkuMaster.Desktop.Images.ImagesViewModel Images { get; } = images ?? new();
    public bool IsBusy => Status.IsBusy || Missing.IsBusy || Availability.IsBusy || Images.IsBusy;
    public bool Unsaved => Status.UnsavedResult || Missing.UnsavedResult || Availability.UnsavedResult || Images.Unsaved;
    public object Snapshot() => new
    {
        version = typeof(DesktopBridge).Assembly.GetName().Version!.ToString(3),
        documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        images = Images.Snapshot(),
        availability = new { files = new { site = Availability.SitePath, supplier = Availability.SupplierPath }, settings = Availability.Settings,
            busy = Availability.IsBusy, hasResult = Availability.HasResult, canSave = Availability.CanSave, unsaved = Availability.UnsavedResult,
            message = Availability.Message, warnings = Availability.Warnings, rows = Availability.Rows },
        status = new
        {
            files = new { site = Status.SitePath, oneC = Status.OneCPath, supplier = Status.SupplierPath },
            settings = Status.Settings, busy = Status.IsBusy, hasResult = Status.HasResult, canSave = Status.CanSave,
            unsaved = Status.UnsavedResult, message = Status.Message, warnings = Status.Warnings,
            rows = Status.HasResult ? Status.AllRows.Select(r => new { row = r.RowNumber, sku = r.Sku, current = r.OldStatus, next = r.NewStatus,
                reason = r.Reason, isManual = r.IsManual, changed = r.OldStatus != r.NewStatus,
                becameUnavailable = r.Kind == ChangeKind.Unavailable, againAvailable = r.Kind == ChangeKind.Available }) : [],
            stats = new { @checked = Status.Summary?.Total ?? 0, unavailable = Status.Summary?.Unavailable ?? 0, again = Status.Summary?.Available ?? 0,
                changed = Status.Summary?.Changed ?? 0, unchanged = Status.Summary?.Unchanged ?? 0 },
            siteColumns = Status.SiteColumns, oneCSheets = Status.OneCSheets, supplierSheets = Status.SupplierSheets
        },
        missing = new
        {
            files = new { site = Missing.SitePath, supplier = Missing.SupplierPath, exceptions = Missing.ExclusionPath },
            settings = Missing.Settings, busy = Missing.IsBusy, hasResult = Missing.HasResult, canSave = Missing.CanExport,
            canExportExclusions = Missing.CanExportExclusions, canAnalyze = Missing.CanAnalyze,
            unsaved = Missing.UnsavedResult, message = Missing.Message, warnings = Missing.Warnings,
            rows = Missing.AllReviewRows, exceptions = Missing.SavedExclusions,
            stats = new { all = Missing.Summary?.SupplierUnique ?? 0, onsite = Missing.Summary?.OnSite ?? 0,
                excluded = Missing.Summary?.Excluded ?? 0, addable = Missing.Summary?.Products.Count ?? 0 }
        }
    };
    public async Task ExecuteAsync(string module, string action, JsonElement data)
    {
        if (module == "availability") { await ExecuteAvailabilityAsync(action, data); return; }
        var isStatus = module == "status";
        if (module is not ("status" or "missing")) throw new InvalidDataException("Невідомий модуль.");
        if (action == "cancel") { if (isStatus) Status.Cancel(); else Missing.Cancel(); return; }
        if (isStatus ? Status.IsBusy : Missing.IsBusy) throw new InvalidOperationException("Дочекайтеся завершення операції.");
        string Text(string key) => data.GetProperty(key).GetString() ?? "";
        switch (action)
        {
            case "analyze":
                if (isStatus) await Status.AnalyzeAsync(); else await Missing.AnalyzeAsync();
                if (!(isStatus ? Status.HasResult : Missing.HasResult)) throw new InvalidOperationException(isStatus ? Status.Message : Missing.Message);
                break;
            case "edit": if (!isStatus) throw new InvalidDataException("Невідома дія."); Status.EditStatus(data.GetProperty("row").GetInt32(), Text("value")); break;
            case "exclude":
                if (isStatus) throw new InvalidDataException("Невідома дія.");
                var excluded = data.GetProperty("value").GetBoolean();
                Missing.SetExcluded(Text("sku"), excluded);
                if (Missing.SavedExclusions.Contains(Text("sku")) != excluded) throw new InvalidOperationException(Missing.Message);
                break;
            case "remove":
                if (isStatus) throw new InvalidDataException("Невідома дія.");
                var removed = data.GetProperty("skus").EnumerateArray().Select(x => x.GetString()!).ToArray();
                Missing.RemoveExclusions(removed);
                if (removed.Any(Missing.SavedExclusions.Contains)) throw new InvalidOperationException(Missing.Message);
                break;
            case "settings":
                if (isStatus) ApplyStatusSettings(data); else ApplyMissingSettings(data);
                break;
            case "setting":
                var node = JsonSerializer.SerializeToNode(isStatus ? (object)Status.Settings : Missing.Settings, Json)!.AsObject();
                var section = Text("section");
                var key = Text("key");
                var target = section.Length == 0 ? node : node[section] as JsonObject;
                if (target == null || !target.ContainsKey(key)) throw new InvalidDataException("Невідоме налаштування.");
                target[key] = JsonNode.Parse(data.GetProperty("value").GetRawText());
                var changed = JsonSerializer.SerializeToElement(node, Json);
                if (isStatus) ApplyStatusSettings(changed); else ApplyMissingSettings(changed);
                break;
            case "save":
                if (isStatus) await Status.SaveAsync(Text("path")); else await Missing.ExportAsync(Text("path"));
                if (!(isStatus ? Status.LastExportSucceeded : Missing.LastExportSucceeded)) throw new InvalidOperationException(isStatus ? Status.Message : Missing.Message);
                break;
            case "saveExclusions":
                if (isStatus) throw new InvalidDataException("Невідома дія.");
                await Missing.ExportExclusionsAsync(Text("path"));
                if (!Missing.LastExportSucceeded) throw new InvalidOperationException(Missing.Message);
                break;
            default: throw new InvalidDataException("Невідома дія.");
        }
    }
    public async Task PickAsync(string module, string source, string path)
    {
        if (module == "availability")
        {
            if (Availability.IsBusy) throw new InvalidOperationException("Дочекайтеся завершення операції.");
            if (source == "site") Availability.SitePath = path;
            else if (source == "supplier") Availability.SupplierPath = path;
            else throw new InvalidDataException("Невідоме джерело.");
        }
        else if (module == "status")
        {
            if (Status.IsBusy) return;
            switch (source) { case "site": Status.SitePath = path; break; case "oneC": Status.OneCPath = path; break; case "supplier": Status.SupplierPath = path; break; default: throw new InvalidDataException("Невідоме джерело."); }
            await Status.LoadMetadataAsync(source == "oneC" ? "onec" : source);
        }
        else if (module == "missing")
        {
            if (Missing.IsBusy) return;
            switch (source) { case "site": Missing.SitePath = path; break; case "supplier": Missing.SupplierPath = path; break; case "exceptions": await Missing.ImportExclusionsAsync(path); if (!Missing.LastImportSucceeded) throw new InvalidOperationException(Missing.Message); break; default: throw new InvalidDataException("Невідоме джерело."); }
        }
        else throw new InvalidDataException("Невідомий модуль.");
    }
    private async Task ExecuteAvailabilityAsync(string action, JsonElement data)
    {
        if (action == "cancel") { Availability.Cancel(); return; }
        if (Availability.IsBusy) throw new InvalidOperationException("Дочекайтеся завершення операції.");
        switch (action)
        {
            case "analyze": await Availability.AnalyzeAsync(); break;
            case "edit": Availability.Edit(data.GetProperty("rows").EnumerateArray().Select(x => x.GetInt32()).ToArray(), data.GetProperty("value").GetString()!); break;
            case "save":
            case "saveReport": await Availability.SaveAsync(data.GetProperty("path").GetString()!, data.GetProperty("rows").EnumerateArray().Select(x => x.GetInt32()).ToArray(), action == "saveReport"); break;
            case "setting":
                var node = JsonSerializer.SerializeToNode(Availability.Settings, Json)!.AsObject();
                var section = data.GetProperty("section").GetString()!;
                var key = data.GetProperty("key").GetString()!;
                var target = section.Length == 0 ? node : node[section] as JsonObject;
                if (target == null || !target.ContainsKey(key)) throw new InvalidDataException("Невідоме налаштування.");
                target[key] = JsonNode.Parse(data.GetProperty("value").GetRawText());
                Availability.ApplySettings(node.Deserialize<SkuMaster.Desktop.Availability.AvailabilitySettings>(Json)!);
                break;
            default: throw new InvalidDataException("Невідома дія.");
        }
    }
    private void ApplyStatusSettings(JsonElement data)
    {
        var next = data.Deserialize<AppSettings>(Json) ?? throw new InvalidDataException("Некоректні налаштування.");
        StatusCatalog.ValidateRules(next.Rules);
        if (next.Export.Format is not ("csv" or "xlsx")) throw new InvalidDataException("Оберіть CSV або XLSX.");
        bool inputChanged = JsonSerializer.Serialize(new { Status.Settings.Rules, Status.Settings.CsvInput, Status.Settings.OneC, Status.Settings.Supplier }, Json)
            != JsonSerializer.Serialize(new { next.Rules, next.CsvInput, next.OneC, next.Supplier }, Json);
        Status.Settings.Rules = next.Rules; Status.Settings.CsvInput = next.CsvInput; Status.Settings.OneC = next.OneC;
        Status.Settings.Supplier = next.Supplier; Status.Settings.Export = next.Export;
        if (inputChanged) Status.Invalidate();
        Status.PersistSettings();
    }
    private void ApplyMissingSettings(JsonElement data)
    {
        var next = data.Deserialize<MissingModuleSettings>(Json) ?? throw new InvalidDataException("Некоректні налаштування.");
        if (next.OutputFormat is not ("csv" or "xlsx")) throw new InvalidDataException("Оберіть CSV або XLSX.");
        foreach (var options in new[] { next.Site, next.Supplier, next.Exclusions })
            if (options.SkuColumn < 1 || options.FirstDataRow < 1 || options.NameColumn < 1) throw new InvalidDataException("Номери колонок і рядків починаються з 1.");
        bool inputChanged = JsonSerializer.Serialize(new { Missing.Settings.Site, Missing.Settings.Supplier, Missing.Settings.Exclusions }, Json)
            != JsonSerializer.Serialize(new { next.Site, next.Supplier, next.Exclusions }, Json);
        Missing.Settings.Site = next.Site; Missing.Settings.Supplier = next.Supplier; Missing.Settings.Exclusions = next.Exclusions; Missing.Settings.OutputFormat = next.OutputFormat;
        if (inputChanged) Missing.Invalidate();
        Missing.PersistSettings();
    }
}
