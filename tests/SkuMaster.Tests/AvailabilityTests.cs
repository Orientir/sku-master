using SkuMaster.Core;
using SkuMaster.Core.Availability;
using Xunit;

namespace SkuMaster.Tests;

public class AvailabilityTests
{
    [Fact]
    public void MatchesExactSkuAndBulkChangesOnlyChosenRows()
    {
        var result = AvailabilityCheck.Execute(new(["sku", "status"], [ ["001", "Снят с производства"], ["002", "Новинка"], ["003", "Скоро в продаже"]]), [" 001 ", "3"], 1, 2);
        Assert.True(result.Rows[0].Available);
        Assert.False(result.Rows[2].Available);
        var edited = AvailabilityCheck.Edit(result.Rows, [2, 4], "");
        Assert.Equal("", edited[0].Next);
        Assert.Equal("Новинка", edited[1].Next);
        Assert.Equal(2, AvailabilityCheck.Export(edited, true).Rows.Count);
        Assert.Throws<ArgumentException>(() => AvailabilityCheck.Edit(edited, [2], "wrong"));
        Assert.Throws<ArgumentException>(() => AvailabilityCheck.Edit(edited, [2, 999], "Новинка"));
        Assert.Equal("", edited[0].Next);
    }

    [Fact]
    public void DuplicateSkuIsCollapsedAndConflictingStatusesFail()
    {
        var table = new TableData(["sku", "status"], [["001", "Новинка"], ["001", "Новинка"], ["", ""]]);
        var result = AvailabilityCheck.Execute(table, [], 1, 2);
        Assert.Single(result.Rows);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Throws<ArgumentException>(() => AvailabilityCheck.Execute(table with { Rows = [["001", "Новинка"], ["001", "Топ продаж"]] }, [], 1, 2));
    }
}
