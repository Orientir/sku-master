namespace SkuMaster.Core.Availability;

public sealed record AvailabilityRow(int Row, string Sku, string Current, string Next, bool Available)
{
    public bool Changed => Current != Next;
}
public sealed record AvailabilityResult(IReadOnlyList<AvailabilityRow> Rows, IReadOnlyList<string> Warnings);

public static class AvailabilityCheck
{
    public static AvailabilityResult Execute(TableData site, IEnumerable<string> supplier, int skuColumn, int statusColumn, CancellationToken token = default)
    {
        if (skuColumn < 1 || statusColumn < 1 || skuColumn == statusColumn || Math.Max(skuColumn, statusColumn) > site.Headers.Length)
            throw new ArgumentException("Перевірте номери колонок SKU та статусу: вони мають існувати та відрізнятися.");
        var available = supplier.Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var unique = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new List<AvailabilityRow>();
        var warnings = new List<string>();
        for (int i = 0; i < site.Rows.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var sku = site.Rows[i][skuColumn - 1].Trim();
            var status = site.Rows[i][statusColumn - 1];
            var row = i + site.FirstDataRow;
            if (sku.Length == 0) { warnings.Add($"Сайт, рядок {row}: порожній SKU, пропущено."); continue; }
            if (unique.TryGetValue(sku, out var previous))
            {
                if (previous != status) throw new ArgumentException($"SKU «{sku}» має різні статуси у файлі сайту. Виправте повтори перед перевіркою.");
                warnings.Add($"Сайт, рядок {row}: повторний SKU «{sku}», об’єднано."); continue;
            }
            unique.Add(sku, status);
            rows.Add(new(row, sku, status, status, available.Contains(sku)));
        }
        return new(rows, warnings);
    }

    public static IReadOnlyList<AvailabilityRow> Edit(IReadOnlyList<AvailabilityRow> rows, IEnumerable<int> ids, string value)
    {
        StatusCatalog.Validate(value);
        var selected = ids.ToHashSet();
        if (selected.Except(rows.Select(r => r.Row)).Any()) throw new ArgumentException("Вибрані рядки більше не належать результату. Повторіть вибір.");
        return rows.Select(r => selected.Contains(r.Row) ? r with { Next = value } : r).ToArray();
    }

    public static TableData Export(IEnumerable<AvailabilityRow> rows, bool onlyChanged) => new(["sku", "status"],
        rows.Where(r => !onlyChanged || r.Changed).Select(r => new[] { r.Sku, r.Next }).ToArray());
}
