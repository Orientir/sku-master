using System.IO;
using SkuMaster.Desktop.Availability;
using SkuMaster.Infrastructure;
using Xunit;
using static SkuMaster.Desktop.Tests.DesktopBridgeTests;

namespace SkuMaster.Desktop.Tests;

public class AvailabilityWorkflowTests
{
    [Fact]
    public async Task BulkEditExportPersistenceAndInputProtectionAreIndependent()
    {
        var dir = Directory.CreateTempSubdirectory("availability-").FullName;
        try
        {
            var bridge = CreateBridge(dir);
            await bridge.PickAsync("availability", "site", Path.Combine(dir, "site.csv"));
            await bridge.PickAsync("availability", "supplier", Path.Combine(dir, "supplier.xlsx"));
            await bridge.ExecuteAsync("availability", "analyze", Data(new { }));
            Assert.Equal(4, bridge.Availability.Rows.Count);
            Assert.True(bridge.Availability.Rows[1].Available);
            Assert.False(bridge.Availability.Rows[0].Available);
            Assert.False(bridge.Status.HasResult);
            await bridge.ExecuteAsync("availability", "edit", Data(new { rows = new[] { 1 }, value = "Новинка" }));
            await bridge.ExecuteAsync("availability", "edit", Data(new { rows = new[] { 1 }, value = "Временно недоступен" }));
            Assert.False(bridge.Availability.UnsavedResult);
            await bridge.ExecuteAsync("availability", "edit", Data(new { rows = new[] { 1, 3 }, value = "Снят с производства" }));
            Assert.True(bridge.Unsaved);
            await bridge.ExecuteAsync("availability", "edit", Data(new { rows = new[] { 2 }, value = "" }));
            await bridge.ExecuteAsync("availability", "setting", Data(new { section = "export", key = "format", value = "xlsx" }));
            Assert.True(bridge.Availability.HasResult);
            Assert.Equal("xlsx", new AvailabilityViewModel(Path.Combine(dir, "availability-settings.json")).Settings.Export.Format);
            var path = Path.Combine(dir, "changed.xlsx");
            await bridge.ExecuteAsync("availability", "save", Data(new { path, rows = new[] { 1, 2, 3, 4 } }));
            using (var book = NPOI.SS.UserModel.WorkbookFactory.Create(path))
            {
                var sheet = book.GetSheetAt(0);
                Assert.Equal(3, sheet.LastRowNum);
                Assert.Equal("001", sheet.GetRow(1).GetCell(0).StringCellValue);
                Assert.Equal("Снят с производства", sheet.GetRow(1).GetCell(1).StringCellValue);
                Assert.Equal("", sheet.GetRow(2).GetCell(1).StringCellValue);
            }
            Assert.False(bridge.Availability.UnsavedResult);
            await bridge.ExecuteAsync("availability", "setting", Data(new { section = "export", key = "format", value = "csv" }));
            var report = Path.Combine(dir, "report.csv");
            await bridge.ExecuteAsync("availability", "saveReport", Data(new { path = report, rows = new[] { 2 } }));
            Assert.Contains("Є у постачальника", File.ReadAllText(report));
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.ExecuteAsync("availability", "save", Data(new { path = bridge.Availability.SitePath, rows = new[] { 1, 2, 3 } })));
            await Assert.ThrowsAsync<ArgumentException>(() => bridge.ExecuteAsync("availability", "edit", Data(new { rows = new[] { 1 }, value = "bad" })));
            await bridge.ExecuteAsync("availability", "setting", Data(new { section = "site", key = "hasHeader", value = true }));
            Assert.False(bridge.Availability.HasResult);
            Assert.Empty(bridge.Availability.Rows);
        }
        finally { Directory.Delete(dir, true); }
    }
}
