using System.IO;
using NPOI.XSSF.UserModel;
using SkuMaster.Desktop.MissingProducts;
using SkuMaster.Infrastructure.MissingProducts;
using SkuMaster.Core.MissingProducts;
using Xunit;

namespace SkuMaster.Desktop.Tests;

public sealed class MissingProductsWorkflowTests
{
    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task ExportExclusionsIncludesHiddenRowsWithoutClearingUnsavedSearch(string format)
    {
        var dir = Directory.CreateTempSubdirectory("exclusions-export-").FullName;
        try
        {
            var model = CreateModel(dir);
            await model.AnalyzeAsync();
            model.SetExcluded("002", true); model.SetExcluded("003", true);
            model.ExclusionSearch = "002";
            model.Settings.OutputFormat = format;
            var path = Path.Combine(dir, "exclusions." + format);
            await model.ExportExclusionsAsync(path);
            Assert.True(model.UnsavedResult);
            var files = new MissingProductsFileService();
            var options = new MissingFileOptions { FirstDataRow = 2 };
            var rows = format == "csv" ? files.ReadSite(path, options) : files.ReadExclusions(path, options);
            Assert.Equal(new[] { "002", "003" }, rows);
            if (format == "xlsx")
            {
                using var book = new XSSFWorkbook(path);
                Assert.Equal(1, book.GetSheetAt(0).GetRow(0).LastCellNum);
            }
            var original = File.ReadAllBytes(model.SupplierPath);
            await model.ExportExclusionsAsync(model.SupplierPath);
            Assert.Equal(original, File.ReadAllBytes(model.SupplierPath));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task SummaryScopesAndReversibleCheckboxesDoNotChangeExportScope()
    {
        var dir = Directory.CreateTempSubdirectory("missing-checkbox-").FullName;
        try
        {
            var model = CreateModel(dir);
            await model.ImportExclusionsAsync(Path.Combine(dir, "missing-exclusions.xlsx"));
            await model.AnalyzeAsync();
            Assert.Equal(new[] { "002", "004" }, model.Rows.Select(x => x.Sku));
            model.SetExcluded("002", true);
            Assert.True(model.Rows.Single(x => x.Sku == "002").IsExcluded);
            Assert.Equal(2, model.Summary!.Excluded);
            model.SetExcluded("002", false);
            Assert.False(model.Rows.Single(x => x.Sku == "002").IsExcluded);
            model.SelectSummary(2);
            Assert.Equal("003", Assert.Single(model.Rows).Sku);
            model.SetExcluded("003", false);
            Assert.False(Assert.Single(model.Rows).IsExcluded);
            Assert.Equal(3, model.Summary.Products.Count);
            model.SelectSummary(1);
            Assert.Equal("001", Assert.Single(model.Rows).Sku);
            var path = Path.Combine(dir, "filtered.csv");
            await model.ExportAsync(path);
            Assert.DoesNotContain("001;", File.ReadAllText(path));
            Assert.Contains("003;Пилка", File.ReadAllText(path));
            model.SelectSummary(0);
            Assert.Equal(4, model.Rows.Count);
            model.SelectSummary(2);
            Assert.Empty(model.Rows);
            Assert.Empty(new ExclusionStore(Path.Combine(dir, "saved-exclusions.json")).Load());
        }
        finally { Directory.Delete(dir, true); }
    }
    internal static MissingProductsViewModel CreateModel(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "missing-site.csv"), "001\n");
        WriteBook(Path.Combine(dir, "missing-supplier.xlsx"), ("001", "На сайті"), ("002", "Дриль"), ("003", "Пилка"), ("004", "Шуруповерт"));
        WriteBook(Path.Combine(dir, "missing-exclusions.xlsx"), ("003", ""));
        return new MissingProductsViewModel(Path.Combine(dir, "saved-exclusions.json"), Path.Combine(dir, "missing-settings.json"))
        { SitePath = Path.Combine(dir, "missing-site.csv"), SupplierPath = Path.Combine(dir, "missing-supplier.xlsx") };
    }
    private static void WriteBook(string path, params (string Sku, string Name)[] products)
    {
        using var book = new XSSFWorkbook(); var sheet = book.CreateSheet("Товари");
        for (int i = 0; i < products.Length; i++) { var row = sheet.CreateRow(i); row.CreateCell(0).SetCellValue(products[i].Sku); row.CreateCell(1).SetCellValue(products[i].Name); }
        using var file = File.Create(path); book.Write(file, true);
    }
    [Fact]
    public async Task ExclusionsPersistAcrossSessionsAndSearchDoesNotLimitExport()
    {
        var dir = Directory.CreateTempSubdirectory("missing-flow-").FullName;
        try
        {
            var model = CreateModel(dir);
            await model.ImportExclusionsAsync(Path.Combine(dir, "missing-exclusions.xlsx"));
            await model.AnalyzeAsync();
            Assert.True(model.HasResult, model.Message);
            Assert.Equal(new[] { "002", "004" }, model.Products.Select(x => x.Sku));
            Assert.Equal(1, model.Summary!.Excluded);
            model.Search = "Дриль";
            Assert.Single(model.Products);
            var path = Path.Combine(dir, "missing.csv");
            await model.ExportAsync(path);
            var lines = File.ReadAllText(path);
            Assert.Contains("004;Шуруповерт", lines);
            Assert.Contains("002;Дриль", lines);
            Assert.False(model.UnsavedResult);
            model.AddExclusions(model.Products);
            Assert.True(model.UnsavedResult);
            Assert.Equal(2, model.ExclusionCount);
            Assert.Empty(model.Products);
            var restarted = new MissingProductsViewModel(Path.Combine(dir, "saved-exclusions.json"), Path.Combine(dir, "missing-settings.json"))
            { SitePath = model.SitePath, SupplierPath = model.SupplierPath };
            await restarted.AnalyzeAsync();
            Assert.Equal("004", Assert.Single(restarted.Products).Sku);
            restarted.RemoveExclusions(["002"]);
            Assert.Equal(new[] { "002", "004" }, restarted.Products.Select(x => x.Sku));
            restarted.Settings.OutputFormat = "xlsx";
            await restarted.ExportAsync(Path.Combine(dir, "missing.xlsx"));
            Assert.Equal(2, new MissingProductsFileService().ReadSupplier(Path.Combine(dir, "missing.xlsx"), new MissingFileOptions { FirstDataRow = 2 }).Count);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task FailedImportKeepsExclusionsAndChangedSourcePreventsStaleExport()
    {
        var dir = Directory.CreateTempSubdirectory("missing-failure-").FullName;
        try
        {
            var model = CreateModel(dir);
            await model.ImportExclusionsAsync(Path.Combine(dir, "missing-exclusions.xlsx"));
            await model.AnalyzeAsync();
            File.WriteAllText(Path.Combine(dir, "broken.xlsx"), "not excel");
            await model.ImportExclusionsAsync(Path.Combine(dir, "broken.xlsx"));
            Assert.Equal(new[] { "003" }, model.Exclusions);
            Assert.True(model.HasResult);
            File.AppendAllText(model.SitePath, "002\n");
            var output = Path.Combine(dir, "stale.csv");
            await model.ExportAsync(output);
            Assert.False(File.Exists(output));
            Assert.False(model.HasResult);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void CorruptStoredExclusionsBlockSearchUntilReimported()
    {
        var dir = Directory.CreateTempSubdirectory("missing-corrupt-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "saved-exclusions.json"), "broken");
            var model = CreateModel(dir);
            Assert.False(model.CanAnalyze);
            Assert.Equal("broken", File.ReadAllText(Path.Combine(dir, "saved-exclusions.json")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
