using System.IO;
using System.Text;
using System.Text.Json;
using NPOI.XSSF.UserModel;
using SkuMaster.Core;
using SkuMaster.Desktop.Web;
using SkuMaster.Infrastructure;
using SkuMaster.Infrastructure.MissingProducts;
using Xunit;

namespace SkuMaster.Desktop.Tests;

public sealed class DesktopBridgeTests
{
    internal static DesktopBridge CreateBridge(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "site.csv"), "001;Временно недоступен\n002;Временно недоступен\n003;Новинка\n004;Топ продаж\n", new UTF8Encoding(true));
        WriteBook(Path.Combine(dir, "onec.xlsx"), "001", "004");
        WriteBook(Path.Combine(dir, "supplier.xlsx"), "002");
        var status = new MainViewModel(new SettingsStore(Path.Combine(dir, "settings.json")))
        { SitePath = Path.Combine(dir, "site.csv"), OneCPath = Path.Combine(dir, "onec.xlsx"), SupplierPath = Path.Combine(dir, "supplier.xlsx") };
        return new DesktopBridge(status, MissingProductsWorkflowTests.CreateModel(dir), new SkuMaster.Desktop.Availability.AvailabilityViewModel(Path.Combine(dir, "availability-settings.json")), new SkuMaster.Desktop.Images.ImagesViewModel(Path.Combine(dir,"images")));
    }
    private static void WriteBook(string path, params string[] skus)
    {
        using var book = new XSSFWorkbook(); var sheet = book.CreateSheet("Товари");
        for (int i = 0; i < skus.Length; i++) sheet.CreateRow(i).CreateCell(0).SetCellValue(skus[i]);
        using var file = File.Create(path); book.Write(file, true);
    }
    internal static JsonElement Data(object value) => JsonSerializer.SerializeToElement(value, DesktopBridge.Json);
    [Fact]
    public async Task BridgeUsesRealRowsPreservesLeadingZerosAndExportsEditedStatuses()
    {
        var dir = Directory.CreateTempSubdirectory("bridge-status-").FullName;
        try
        {
            var bridge = CreateBridge(dir);
            await bridge.ExecuteAsync("status", "analyze", Data(new { }));
            var state = Data(bridge.Snapshot()).GetProperty("status");
            Assert.Equal(4, state.GetProperty("rows").GetArrayLength());
            Assert.Equal("001", state.GetProperty("rows")[0].GetProperty("sku").GetString());
            Assert.Equal("Знайдено в 1С", state.GetProperty("rows")[0].GetProperty("reason").GetString());
            Assert.Equal("Знайдено у постачальника", state.GetProperty("rows")[1].GetProperty("reason").GetString());
            await bridge.ExecuteAsync("status", "edit", Data(new { row = 3, value = "" }));
            var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(bridge.Status.Settings))!;
            settings.Export.Format = "xlsx"; settings.Export.SkuHeader = "Артикул";
            settings.Export.StatusHeader = "Мій статус"; settings.Export.OnlyChanged = false;
            settings.Rules.ReplacementStatus = "";
            // A rule change requires another analysis; an output change does not.
            await bridge.ExecuteAsync("status", "settings", Data(settings));
            Assert.False(bridge.Status.HasResult);
            await bridge.ExecuteAsync("status", "analyze", Data(new { }));
            await bridge.ExecuteAsync("status", "edit", Data(new { row = 3, value = "" }));
            settings.Export.Format = "csv";
            settings.Export.Delimiter = "\t";
            await bridge.ExecuteAsync("status", "settings", Data(settings));
            Assert.True(bridge.Status.HasResult);
            var path = Path.Combine(dir, "result.csv");
            await bridge.ExecuteAsync("status", "save", Data(new { path }));
            Assert.True(bridge.Status.LastExportSucceeded);
            var output = new FileService().ReadSite(path, new() { HasHeader = true, Delimiter = "\t" });
            Assert.Equal(new[] { "Артикул", "Мій статус" }, output.Headers);
            Assert.Equal(4, output.Rows.Count);
            Assert.Equal("", output.Rows[2][1]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ExecuteAsync("status", "save", Data(new { path = bridge.Status.SitePath })));
            Assert.False(bridge.Status.LastExportSucceeded);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task BridgePersistsExceptionsAndReportsFailedImportsWithoutReplacingThem()
    {
        var dir = Directory.CreateTempSubdirectory("bridge-missing-").FullName;
        try
        {
            var bridge = CreateBridge(dir);
            await bridge.ExecuteAsync("missing", "analyze", Data(new { }));
            await bridge.ExecuteAsync("missing", "exclude", Data(new { sku = "002", value = true }));
            Assert.Contains("002", new ExclusionStore(Path.Combine(dir, "saved-exclusions.json")).Load());
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.PickAsync("missing", "exceptions", Path.Combine(dir, "absent.xlsx")));
            Assert.Contains("002", bridge.Missing.SavedExclusions);
            await bridge.PickAsync("missing", "exceptions", Path.Combine(dir, "missing-exclusions.xlsx"));
            Assert.Equal(new[] { "003" }, bridge.Missing.SavedExclusions);
            await bridge.ExecuteAsync("missing", "remove", Data(new { skus = new[] { "003" } }));
            Assert.Empty(new ExclusionStore(Path.Combine(dir, "saved-exclusions.json")).Load());
            await bridge.ExecuteAsync("missing", "exclude", Data(new { sku = "004", value = true }));
            var path = Path.Combine(dir, "exceptions.csv");
            await bridge.ExecuteAsync("missing", "saveExclusions", Data(new { path }));
            Assert.Contains("004", File.ReadAllText(path));
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ExecuteAsync("missing", "saveExclusions", Data(new { path = bridge.Missing.SupplierPath })));
            Assert.True(bridge.Missing.UnsavedResult);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task BridgeRejectsUnknownCommandsAndInvalidStatuses()
    {
        var dir = Directory.CreateTempSubdirectory("bridge-validation-").FullName;
        try
        {
            var bridge = CreateBridge(dir);
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.ExecuteAsync("shell", "run", Data(new { })));
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.ExecuteAsync("status", "run", Data(new { })));
            bridge.Status.Settings.Rules.UnavailableStatus = "invalid";
            await Assert.ThrowsAsync<ArgumentException>(() => bridge.ExecuteAsync("status", "settings", Data(bridge.Status.Settings)));
        }
        finally { Directory.Delete(dir, true); }
    }
}
