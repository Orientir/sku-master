using SkuMaster.Core;
using Xunit;

namespace SkuMaster.Tests;
public sealed class ExportScopeTests
{
    [Fact]
    public void ChangedOnlyDefaultsOnAndMigratesOnceWhilePreservingLaterChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), "sku-default-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SkuMaster.Infrastructure.SettingsStore(path);
            Assert.True(store.Load().Settings.Export.OnlyChanged);
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"Export\":{\"OnlyChanged\":false,\"Format\":\"xlsx\"}}");
            var migrated = store.Load().Settings;
            Assert.True(migrated.Export.OnlyChanged);
            Assert.Equal("xlsx", migrated.Export.Format);
            migrated.Export.OnlyChanged = false;
            store.Save(migrated);
            Assert.False(store.Load().Settings.Export.OnlyChanged);
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ChangedOnlyKeepsChangedRowsInOrderEvenWithDuplicateSkus(int firstRow)
    {
        var input = new TableData(["sku", "status", "extra"],
            [new[] { "A", "Временно недоступен", "first" }, new[] { "A", "Акція", "second" }, new[] { "B", "", "third" }], FirstDataRow: firstRow);
        var result = new StatusOperation().Execute(input, new HashSet<string> { "A" }, new());
        var changed = result.GetExportTable(true);
        Assert.Equal(2, changed.Rows.Count);
        Assert.Equal(new[] { "A", "", "first" }, changed.Rows[0]);
        Assert.Equal(new[] { "B", "Временно недоступен", "third" }, changed.Rows[1]);
        Assert.Equal(3, result.GetExportTable(false).Rows.Count);
        Assert.Equal(3, result.Output.Rows.Count);
        Assert.Equal(input.Headers, changed.Headers);
    }
    [Fact]
    public void ChangedOnlyWithNoChangesReturnsEmptyTableWithHeaders()
    {
        var result = new StatusOperation().Execute(new(["sku", "status"], [new[] { "A", "Акція" }]), new HashSet<string> { "A" }, new());
        Assert.Empty(result.GetExportTable(true).Rows);
        Assert.Equal(new[] { "sku", "status" }, result.GetExportTable(true).Headers);
        Assert.Single(result.GetExportTable(false).Rows);
    }
}
