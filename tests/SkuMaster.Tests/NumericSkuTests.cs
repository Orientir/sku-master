using NPOI.XSSF.UserModel;
using SkuMaster.Infrastructure;
using Xunit;

namespace SkuMaster.Tests;
public sealed class NumericSkuTests
{
    [Theory]
    [InlineData(123456789012d, "123456789012")]
    [InlineData(123456789012345d, "123456789012345")]
    public void GeneralNumericSkuPreservesFullInteger(double numeric, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            using (var book = new XSSFWorkbook())
            {
                book.CreateSheet("SKU").CreateRow(0).CreateCell(0).SetCellValue(numeric);
                using var output = File.Create(path);
                book.Write(output, true);
            }
            var result = new FileService().ReadSource(path, new());
            Assert.Contains(expected, result.Skus);
        }
        finally { File.Delete(path); }
    }
}
