using System.Text;
using NPOI.XSSF.UserModel;
using SkuMaster.Infrastructure;
using Xunit;

namespace SkuMaster.Tests;
public sealed class SharedFileReadTests
{
    [Fact]
    public void SnapshotBlocksConcurrentWritesAndReleasesLockAfterReading()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            File.WriteAllText(path, "001,");
            using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1);
            using (var snapshot = new SharedInputFile(path))
            {
                Assert.Equal((int)'0', snapshot.Stream.ReadByte());
                Assert.Throws<IOException>(() => { editor.WriteByte((byte)'9'); editor.Flush(); });
            }
            editor.Position = 0;
            editor.WriteByte((byte)'0');
            editor.Flush();
            Assert.Equal(4, editor.Length);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void CsvCanBeReadWhileAnotherAppHasAWriteHandle()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            File.WriteAllText(path, "001,\r\n002,Временно недоступен", new UTF8Encoding(true));
            using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var table = new FileService().ReadSite(path, new());
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("001", table.Rows[0][0]);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ExcelCanBeReadWhileAnotherAppHasAWriteHandle()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            using (var book = new XSSFWorkbook())
            {
                book.CreateSheet("SKU").CreateRow(0).CreateCell(0).SetCellValue("001");
                using var stream = File.Create(path);
                book.Write(stream, true);
            }
            using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var service = new FileService();
            Assert.Equal(new[] { "SKU" }, service.GetSheets(path));
            Assert.Contains("001", service.ReadSource(path, new()).Skus);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ExclusiveLockHasActionableError()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            File.WriteAllText(path, "001,");
            using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var error = Assert.Throws<InvalidDataException>(() => new FileService().ReadSite(path, new()));
            Assert.Contains("іншій програмі", error.Message);
            Assert.Contains("Закрийте", error.Message);
        }
        finally { File.Delete(path); }
    }
}
