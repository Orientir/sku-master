using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SkuMaster.Core.MissingProducts;
using SkuMaster.Infrastructure.MissingProducts;
using Xunit;

namespace SkuMaster.Tests;

public sealed class MissingProductsFilesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "missing-tests-" + Guid.NewGuid().ToString("N"));
    public MissingProductsFilesTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void SingleColumnCsvHonorsFirstDataRowAndZeros()
    {
        var path = Path.Combine(directory, "site.csv");
        File.WriteAllText(path, "Артикул\r\n001\r\n 002 \r\n", new UTF8Encoding(true));
        var service = new MissingProductsFileService();
        Assert.Equal(["001", "002"], service.ReadSite(path, new() { FirstDataRow = 2 }));
        Assert.Equal("Артикул", service.ReadSite(path, new())[0]);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(path, "Артикул;Назва\r\n001;Товар", Encoding.GetEncoding(1251));
        Assert.Equal(["001"], service.ReadSite(path, new() { FirstDataRow = 2 }));
    }

    [Theory]
    [InlineData("xls")]
    [InlineData("xlsx")]
    public void ExcelReadsTextNumericAndMaskedSku(string extension)
    {
        var path = Path.Combine(directory, "source." + extension);
        using (IWorkbook book = extension == "xls" ? new HSSFWorkbook() : new XSSFWorkbook())
        {
            var sheet = book.CreateSheet("Товари");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("Артикул");
            var first = sheet.CreateRow(1);
            first.CreateCell(0).SetCellValue("001"); first.CreateCell(1).SetCellValue("Назва");
            sheet.CreateRow(2).CreateCell(0).SetCellValue(123456789012d);
            var masked = sheet.CreateRow(3).CreateCell(0); masked.SetCellValue(12d);
            masked.CellStyle = book.CreateCellStyle(); masked.CellStyle.DataFormat = book.CreateDataFormat().GetFormat("00000");
            using var output = File.Create(path); book.Write(output, true);
        }
        var service = new MissingProductsFileService();
        var options = new MissingFileOptions { FirstDataRow = 2, SheetName = "Товари" };
        var products = service.ReadSupplier(path, options);
        Assert.Equal(["001", "123456789012", "00012"], products.Select(p => p.Sku));
        Assert.Equal("Назва", products[0].Name);
        Assert.Equal(products.Select(p => p.Sku), service.ReadExclusions(path, options));
    }

    [Fact]
    public void ExclusionsReplaceAndInvalidLoadDoesNotModifyFile()
    {
        var path = Path.Combine(directory, "exclusions.json");
        var store = new ExclusionStore(path);
        store.Save([" 001 ", "001", "002"]);
        Assert.Equal(["001", "002"], store.Load());
        IEnumerable<string> Broken() { yield return "new"; throw new IOException("failure"); }
        Assert.Throws<IOException>(() => store.Save(Broken()));
        Assert.Equal(["001", "002"], store.Load());
        store.Save(["003"]);
        Assert.Equal(["003"], store.Load());
        File.WriteAllText(path, "broken");
        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.Equal("broken", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public void ExportPreservesZerosAndProtectsInputs(string format)
    {
        var path = Path.Combine(directory, "result." + format);
        var service = new MissingProductsFileService();
        service.Export([new("001", "Назва;товару")], path, format, []);
        Assert.Throws<InvalidDataException>(() => service.Export([], path, format, [path]));
        if (format == "csv")
        {
            Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
            Assert.Equal(["001"], service.ReadSite(path, new() { FirstDataRow = 2, Delimiter = ";" }));
        }
        else Assert.Equal("001", service.ReadSupplier(path, new() { FirstDataRow = 2 })[0].Sku);
    }

    [Fact]
    public void RejectsAbsentExcelColumnsAndEvaluatesNames()
    {
        var path = Path.Combine(directory, "formulas.xlsx");
        using (var book = new XSSFWorkbook())
        {
            var row = book.CreateSheet().CreateRow(0);
            row.CreateCell(0).SetCellValue("001");
            row.CreateCell(1).SetCellFormula("\"Назва\"");
            using var stream = File.Create(path); book.Write(stream, true);
        }
        var service = new MissingProductsFileService();
        Assert.Equal("Назва", service.ReadSupplier(path, new())[0].Name);
        Assert.Throws<InvalidDataException>(() => service.ReadExclusions(path, new() { SkuColumn = 5 }));
        Assert.Throws<InvalidDataException>(() => service.ReadSupplier(path, new() { NameColumn = 5 }));
        Assert.Throws<InvalidDataException>(() => service.ReadExclusions(path, new() { FirstDataRow = 5 }));
        Assert.Throws<InvalidDataException>(() => service.ReadExclusions(path, new() { SkuColumn = 2 }));
    }

    [Fact]
    public void RejectsMalformedCsvAndWrongExportExtension()
    {
        var path = Path.Combine(directory, "site.csv");
        var service = new MissingProductsFileService();
        File.WriteAllText(path, "001;Name\r\n002;Other;Extra");
        Assert.Throws<InvalidDataException>(() => service.ReadSite(path, new()));
        File.WriteAllText(path, "\r\n");
        Assert.Throws<InvalidDataException>(() => service.ReadSite(path, new()));
        Assert.Throws<InvalidDataException>(() => service.Export([], Path.Combine(directory, "exclusions.json"), "csv", []));
    }
}
