using SkuMaster.Core;
using Xunit;

namespace SkuMaster.Tests;
public sealed class ChangeQueryTests
{
    private static readonly StatusChange[] Rows =
    [
        new(1, "AbC-001", "Временно недоступен", "", ChangeKind.Available),
        new(2, "002", "Акція", "Временно недоступен", ChangeKind.Unavailable),
        new(3, "003", "", "Новинка", ChangeKind.Available)
    ];
    [Theory]
    [InlineData(" abc- ", 1)]
    [InlineData("АКЦІЯ", 2)]
    [InlineData("новин", 3)]
    public void FindsSkuAndStatusesIgnoringCaseAndOuterSpaces(string search, int row)
        => Assert.Equal(row, Assert.Single(ChangeQuery.Apply(Rows, search)).RowNumber);

    [Fact]
    public void CombinesSearchTypeAndBothStatuses()
    {
        Assert.Equal(2, Assert.Single(ChangeQuery.Apply(Rows, "недоступен", 1, "Акція", "Временно недоступен")).RowNumber);
        Assert.Empty(ChangeQuery.Apply(Rows, "003", 1));
        Assert.Empty(ChangeQuery.Apply(Rows, "", 2, "Акція"));
    }
    [Fact]
    public void EmptyStatusIsDistinctFromAllStatuses()
    {
        Assert.Equal(3, Assert.Single(ChangeQuery.Apply(Rows, "", oldStatus: "")).RowNumber);
        Assert.Equal(1, Assert.Single(ChangeQuery.Apply(Rows, "", newStatus: "")).RowNumber);
        Assert.Equal(3, ChangeQuery.Apply(Rows, "").Count);
        Assert.Equal(3, Rows.Length);
    }
    [Fact]
    public void NoMatchesReturnsEmptyWithoutChangingOriginalRows()
    {
        Assert.Empty(ChangeQuery.Apply(Rows, "missing"));
        Assert.Equal("Новинка", Rows[2].NewStatus);
    }
}
