using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NPOI.XSSF.UserModel;
using SkuMaster.Core;
using SkuMaster.Desktop;
using SkuMaster.Desktop.MissingProducts;
using SkuMaster.Infrastructure;
using Xunit;

namespace SkuMaster.Desktop.Tests;

[Collection("Desktop UI")]
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
}
