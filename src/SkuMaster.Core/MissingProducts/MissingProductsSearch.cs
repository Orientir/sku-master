namespace SkuMaster.Core.MissingProducts;

public sealed record MissingProduct(string Sku, string Name);
public sealed record MissingSearchResult(IReadOnlyList<MissingProduct> Products, int SupplierUnique, int OnSite, int Excluded, IReadOnlyList<string> Warnings);

public static class MissingProductsSearch
{
    public static MissingSearchResult Execute(IReadOnlyList<MissingProduct> supplier, IEnumerable<string> site, IEnumerable<string> exclusions, CancellationToken token = default)
    {
        HashSet<string> Normalize(IEnumerable<string> input)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in input) { token.ThrowIfCancellationRequested(); if (!string.IsNullOrWhiteSpace(value)) set.Add(value.Trim()); }
            return set;
        }
        var onSite = Normalize(site);
        var excluded = Normalize(exclusions);
        var unique = new Dictionary<string, string>(StringComparer.Ordinal);
        var products = new List<MissingProduct>();
        var warnings = new List<string>();
        int siteCount = 0, excludedCount = 0;
        for (int i = 0; i < supplier.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var item = supplier[i];
            var sku = item.Sku.Trim();
            if (sku.Length == 0) { warnings.Add($"Позиція {i + 1}: порожній артикул, рядок пропущено."); continue; }
            if (unique.TryGetValue(sku, out var name))
            {
                if (!string.Equals(name, item.Name, StringComparison.Ordinal)) warnings.Add($"Артикул «{sku}» має різні назви; використано першу назву.");
                continue;
            }
            unique.Add(sku, item.Name);
            if (onSite.Contains(sku)) siteCount++;
            else if (excluded.Contains(sku)) excludedCount++;
            else products.Add(new(sku, item.Name));
        }
        return new(products, unique.Count, siteCount, excludedCount, warnings);
    }
}

public sealed class MissingFileOptions
{
    public int SkuColumn { get; set; } = 1;
    public int NameColumn { get; set; } = 2;
    public int FirstDataRow { get; set; } = 1;
    public string SheetName { get; set; } = "";
    public string Delimiter { get; set; } = "auto";
    public string EncodingName { get; set; } = "auto";
}

public sealed class MissingModuleSettings
{
    public MissingFileOptions Site { get; set; } = new();
    public MissingFileOptions Supplier { get; set; } = new();
    public MissingFileOptions Exclusions { get; set; } = new();
    public string OutputFormat { get; set; } = "csv";
}
