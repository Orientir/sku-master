using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using SkuMaster.Core;
using SkuMaster.Infrastructure;
using Xunit;
namespace SkuMaster.Tests;
public sealed class FileServiceTests : IDisposable {
 readonly string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
 readonly FileService service=new();
 public FileServiceTests()=>Directory.CreateDirectory(dir);
 string P(string name)=>Path.Combine(dir,name);
 public void Dispose()=>Directory.Delete(dir,true);
 [Fact] public void CsvPreservesQuotedFieldsAndExtraColumns(){
 File.WriteAllText(P("a.csv"),"sku;status;other\r\n001;\"a;b\";\"line1\nline2\"\r\n002;;\"a\"\"b\"",new UTF8Encoding(true));
 var table=service.ReadSite(P("a.csv"),new(){HasHeader=true});
 Assert.Equal("001",table.Rows[0][0]); Assert.Equal("a;b",table.Rows[0][1]); Assert.Equal("line1\nline2",table.Rows[0][2]); Assert.Equal("a\"b",table.Rows[1][2]);
 }
 [Theory] [InlineData(false)] [InlineData(true)] public void ExcelHonorsMaskHeaderAndDuplicates(bool xls){
 using IWorkbook book=xls?new HSSFWorkbook():new XSSFWorkbook(); var sheet=book.CreateSheet("Товари"); sheet.CreateRow(0).CreateCell(0).SetCellValue("sku");
 var cell=sheet.CreateRow(1).CreateCell(0);cell.SetCellValue(12);var style=book.CreateCellStyle();style.DataFormat=book.CreateDataFormat().GetFormat("0000");cell.CellStyle=style;
 sheet.CreateRow(2).CreateCell(0).SetCellValue(" 0012 "); using(var stream=File.Create(P("b"+(xls?".xls":".xlsx"))))book.Write(stream,true);
 var path=P("b"+(xls?".xls":".xlsx"));var result=service.ReadSource(path,new(){HasHeader=true}); Assert.Equal(new[]{"0012"},result.Skus);Assert.Single(result.Warnings);Assert.Contains("3",result.Warnings[0]);Assert.Equal(new[]{"Товари"},service.GetSheets(path));
 }
 [Fact] public void RejectsFormula(){using var book=new XSSFWorkbook();book.CreateSheet().CreateRow(0).CreateCell(0).SetCellFormula("1+1");using(var s=File.Create(P("b.xlsx")))book.Write(s,true);Assert.Throws<InvalidDataException>(()=>service.ReadSource(P("b.xlsx"),new()));}
 [Theory] [InlineData("sku;status\n001;\"broken")] [InlineData("sku;status\n001;ok;extra")] [InlineData("sku,status;other\n001,ok;x")] public void RejectsInvalidOrAmbiguousCsv(string data){File.WriteAllText(P("a.csv"),data);Assert.Throws<InvalidDataException>(()=>service.ReadSite(P("a.csv"),new(){HasHeader=true}));}
 [Fact] public void ExportRoundTripAndAtomicFailure(){var table=new TableData(["sku","status","extra"],new[]{new[]{"001","Новинка","😀"}});service.Export(table,P("out.csv"),new(){SkuHeader="код",StatusHeader="стан"},[]);var reread=service.ReadSite(P("out.csv"),new(){HasHeader=true});Assert.Equal(new[]{"код","стан","extra"},reread.Headers);Assert.Equal(table.Rows[0],reread.Rows[0]);var before=File.ReadAllBytes(P("out.csv"));Assert.Throws<InvalidDataException>(()=>service.Export(table,P("out.csv"),new(){EncodingName="windows-1251"},[]));Assert.Equal(before,File.ReadAllBytes(P("out.csv")));Assert.Throws<InvalidDataException>(()=>service.Export(table,P("out.csv"),new(),[P("out.csv")]));}
 [Fact] public void XlsxExportKeepsFormulaLikeText(){service.Export(new(["sku","status"],new[]{new[]{"=1+1",""}}),P("out.xlsx"),new(){Format="xlsx",IncludeHeaders=false},[]);Assert.Contains("=1+1",service.ReadSource(P("out.xlsx"),new()).Skus);}
 [Fact] public void SettingsRoundTripAndCorruption(){var store=new SettingsStore(P("settings.json"));var settings=new AppSettings();settings.OneC.HasHeader=true;store.Save(settings);Assert.True(store.Load().Settings.OneC.HasHeader);Assert.Null(store.Load().Warning);File.WriteAllText(P("settings.json"),"broken");Assert.NotNull(store.Load().Warning);File.WriteAllText(P("settings.json"),"{\"SchemaVersion\":99}");Assert.NotNull(store.Load().Warning);}
}
