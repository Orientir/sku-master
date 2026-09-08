using System.Text;
using SkuMaster.Core;
using SkuMaster.Infrastructure;
using Xunit;
namespace SkuMaster.Tests;
public sealed class FileSafetyTests : IDisposable {
 readonly string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
 public FileSafetyTests()=>Directory.CreateDirectory(dir);
 string P(string name)=>Path.Combine(dir,name);
 public void Dispose()=>Directory.Delete(dir,true);
 [Fact] public void Windows1251RequiresExplicitSelection(){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);File.WriteAllBytes(P("a.csv"),Encoding.GetEncoding(1251).GetBytes("sku;status\n001;Новинка"));var service=new FileService();Assert.Throws<InvalidDataException>(()=>service.ReadSite(P("a.csv"),new()));Assert.Equal("Новинка",service.ReadSite(P("a.csv"),new(){EncodingName="windows-1251"}).Rows[0][1]);}
 [Fact] public void CancellationDoesNotReplaceDestination(){var path=P("out.csv");File.WriteAllText(path,"original");using var cts=new CancellationTokenSource();cts.Cancel();Assert.Throws<OperationCanceledException>(()=>new FileService().Export(new(["sku","status"],new[]{new[]{"1","ok"}}),path,new(),[],cts.Token));Assert.Equal("original",File.ReadAllText(path));}
 [Theory] [InlineData("{\"Rules\":null}")] [InlineData("{\"Rules\":{\"UnavailableStatus\":\"\"}}")] [InlineData("{\"CsvInput\":{\"Delimiter\":\"|\"}}")] public void InvalidSettingsRecoverWithWarning(string json){File.WriteAllText(P("settings.json"),json);Assert.NotNull(new SettingsStore(P("settings.json")).Load().Warning);}
 [Fact] public void RenamedHeadersMustNotConflictWithExtras(){var table=new TableData(["sku","status","other"],new[]{new[]{"1","ok","x"}});Assert.Throws<InvalidDataException>(()=>new FileService().Export(table,P("out.csv"),new(){SkuHeader="other"},[]));Assert.False(File.Exists(P("out.csv")));}
}
