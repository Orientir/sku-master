using SkuMaster.Core;
using Xunit;

namespace SkuMaster.Tests;

public sealed class ResultReviewTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EditsPreserveOriginalsDuplicateRowsAndExportActualDifferences(int firstRow)
    {
        var table = new TableData(["sku", "status", "extra"],
            [new[] { "001", "Временно недоступен", "first" }, new[] { "001", "Лучшая цена", "second" }, new[] { "002", "", "third" }], FirstDataRow: firstRow);
        var processed = new StatusOperation().Execute(table, new HashSet<string> { "001" }, new());
        var review = new ResultReview(processed, "sku", "status", "Временно недоступен");
        Assert.Equal(3, review.Rows.Count);
        Assert.Single(ChangeQuery.Apply(review.Rows, "", 4));
        review.SetStatus(firstRow + 1, "Новинка");
        Assert.Equal("", review.Result.Output.Rows[0][1]);
        Assert.Equal("Новинка", review.Result.Output.Rows[1][1]);
        Assert.Equal("Лучшая цена", review.Rows[1].OldStatus);
        Assert.Equal(3, review.Result.Changed);
        Assert.Equal(1, review.Result.Available);
        Assert.Equal(1, review.Result.Unavailable);
        Assert.Equal(3, ChangeQuery.Apply(review.Rows, "", 0).Count);
        review.SetStatus(firstRow, "Временно недоступен");
        Assert.Equal(0, review.Result.Available);
        Assert.Equal(1, review.Result.Unchanged);
        Assert.Equal(new[] { "second", "third" }, review.Result.GetExportTable(true).Rows.Select(x => x[2]));
        review.SetStatus(firstRow + 1, "Лучшая цена");
        review.SetStatus(firstRow + 2, "");
        Assert.Empty(review.Result.GetExportTable(true).Rows);
        Assert.Equal(3, ChangeQuery.Apply(review.Rows, "", 3).Count);
        Assert.Equal(3, ChangeQuery.Apply(review.Rows, "", 4).Count);
        Assert.Empty(ChangeQuery.Apply(review.Rows, "", 0));
        Assert.Equal("Лучшая цена", processed.Output.Rows[1][1]);
        Assert.Equal("Временно недоступен", table.Rows[0][1]);
    }
}
