using SkuMaster.Core;
using Xunit;

namespace SkuMaster.Tests;

public sealed class StatusOperationTests
{
    private const string Missing = "Временно недоступен";
    private readonly StatusOperation operation = new();
    [Fact]
    public void ReconcilesAvailabilityAndPreservesOriginalAndExtraColumns()
    {
        var input = new TableData(["sku", "status", "title"],
            [new[] { "001", Missing, "Перший" }, new[] { "002", "Акція", "Другий" }, new[] { "003", "", "Третій" }]);
        var result = operation.Execute(input, new HashSet<string> { "001", "002" }, new());
        Assert.Equal("", result.Output.Rows[0][1]);
        Assert.Equal("Акція", result.Output.Rows[1][1]);
        Assert.Equal(Missing, result.Output.Rows[2][1]);
        Assert.Equal("001", result.Output.Rows[0][0]);
        Assert.Equal("Перший", result.Output.Rows[0][2]);
        Assert.Equal(Missing, input.Rows[0][1]);
        Assert.Equal(1, result.Available);
        Assert.Equal(1, result.Unavailable);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(3, result.UniqueSkus);
    }

    [Fact]
    public void CustomReplacementAndAlreadyUnavailableAreCountedOnlyWhenChanged()
    {
        var input = new TableData(["sku", "status"], [new[] { "A", Missing }, new[] { "B", Missing }]);
        var result = operation.Execute(input, new HashSet<string> { "A" },
            new() { ClearFoundStatus = false, ReplacementStatus = "Новинка" });
        Assert.Equal("Новинка", result.Output.Rows[0][1]);
        Assert.Equal(1, result.Available);
        Assert.Equal(0, result.Unavailable);
        Assert.Equal(1, result.Unchanged);
    }

    [Fact]
    public void EmptyAndDuplicateSkusKeepRowsAndGenerateWarnings()
    {
        var input = new TableData(["sku", "status"], [new[] { " A ", Missing }, new[] { "A", Missing }, new[] { " ", "keep" }, new[] { "a", "" }]);
        var result = operation.Execute(input, new HashSet<string> { "A" }, new());
        Assert.Equal(4, result.Total);
        Assert.Equal(2, result.UniqueSkus);
        Assert.Equal(2, result.Available);
        Assert.Equal(1, result.Unavailable);
        Assert.Equal("keep", result.Output.Rows[2][1]);
        Assert.Equal(" A ", result.Output.Rows[0][0]);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void EqualReplacementDoesNotCountAsRestored()
    {
        var result = operation.Execute(new(["sku", "status"], [new[] { "A", Missing }]),
            new HashSet<string> { "A" }, new() { ClearFoundStatus = false, ReplacementStatus = Missing });
        Assert.Empty(result.Changes);
        Assert.Equal(1, result.Unchanged);
    }

    [Fact]
    public void RejectsAmbiguousColumnsAndEmptyUnavailableStatus()
    {
        Assert.Throws<ArgumentException>(() => operation.Execute(new(["sku", "sku", "status"], []), new HashSet<string>(), new()));
        Assert.Throws<ArgumentException>(() => operation.Execute(new(["sku", "status"], []), new HashSet<string>(), new() { UnavailableStatus = " " }));
    }

    [Fact]
    public void CancellationProducesNoPartialResult()
    {
        Assert.Throws<OperationCanceledException>(() => operation.Execute(new(["sku", "status"], [new[] { "A", "" }]),
            new HashSet<string>(), new(), new CancellationToken(true)));
    }
}
