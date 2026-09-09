using SkuMaster.Core.MissingProducts;
using Xunit;

namespace SkuMaster.Tests;

public class MissingProductsTests
{
    [Fact]
    public void DifferencePreservesZerosOrderAndReportsConflicts()
    {
        var result = MissingProductsSearch.Execute([
            new(" 001 ", "First"), new("001", "Other"), new("a", "A"),
            new("A", "Upper"), new("b", "B"), new("", "Blank")], [" a "], ["a", "b"]);
        Assert.Equal(["001", "A"], result.Products.Select(p => p.Sku));
        Assert.Equal("First", result.Products[0].Name);
        Assert.Equal(4, result.SupplierUnique);
        Assert.Equal(1, result.OnSite);
        Assert.Equal(1, result.Excluded);
        Assert.Equal(2, result.Warnings.Count);
    }
}
