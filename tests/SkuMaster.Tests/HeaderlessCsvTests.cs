using SkuMaster.Core;
using SkuMaster.Infrastructure;
using Xunit;

namespace SkuMaster.Tests;
public sealed class HeaderlessCsvTests
{
    [Fact]
    public void DefaultInputKeepsFirstProductAndUsesPositionalColumns()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            File.WriteAllText(path, "001;Временно недоступен;Перший\n002;;Другий");
            var table = new FileService().ReadSite(path, new());
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("sku", table.Headers[0]);
            Assert.Equal("status", table.Headers[1]);
            Assert.Equal("001", table.Rows[0][0]);
            var result = new StatusOperation().Execute(table, new HashSet<string> { "001" }, new());
            Assert.Equal(1, result.Changes[0].RowNumber);
            Assert.Equal("", result.Output.Rows[0][1]);
            Assert.Equal("Временно недоступен", result.Output.Rows[1][1]);
        }
        finally { File.Delete(path); }
    }
}
