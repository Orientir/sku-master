namespace SkuMaster.Core;

public sealed class StatusOperation : ITableOperation
{
    public string Id => "sku-status";
    public string DisplayName => "Оновлення статусів за SKU";
    public OperationResult Execute(TableData input, IReadOnlySet<string> available, RuleSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StatusCatalog.ValidateRules(settings);
        if (string.IsNullOrWhiteSpace(settings.UnavailableStatus))
            throw new ArgumentException("Вкажіть статус для відсутніх товарів.");
        var skuIndex = FindColumn(input.Headers, settings.SkuColumn);
        var statusIndex = FindColumn(input.Headers, settings.StatusColumn);
        if (skuIndex == statusIndex) throw new ArgumentException("Колонки SKU та статусу мають бути різними.");
        var normalizedAvailable = new HashSet<string>(available.Select(x => x.Trim()), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<string[]>(input.Rows.Count);
        var changes = new List<StatusChange>();
        var warnings = new List<string>();
        for (var i = 0; i < input.Rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = input.Rows[i];
            if (original.Length != input.Headers.Length)
                throw new ArgumentException($"Експорт сайту, рядок {i + input.FirstDataRow}: кількість колонок не відповідає заголовкам.");
            var row = (string[])original.Clone();
            rows.Add(row);
            var sku = row[skuIndex].Trim();
            if (sku.Length == 0)
            {
                warnings.Add($"Експорт сайту, рядок {i + input.FirstDataRow}: порожній SKU; статус залишено без змін.");
                continue;
            }
            if (!seen.Add(sku)) warnings.Add($"Експорт сайту, рядок {i + input.FirstDataRow}: повторний SKU «{sku}».");
            var oldStatus = row[statusIndex];
            var found = normalizedAvailable.Contains(sku);
            var newStatus = !found ? settings.UnavailableStatus
                : oldStatus == settings.MatchStatus ? (settings.ClearFoundStatus ? "" : settings.ReplacementStatus) : oldStatus;
            if (newStatus == oldStatus) continue;
            row[statusIndex] = newStatus;
            changes.Add(new(i + input.FirstDataRow, row[skuIndex], oldStatus, newStatus,
                found ? ChangeKind.Available : ChangeKind.Unavailable));
        }
        return new(new((string[])input.Headers.Clone(), rows, input.Delimiter, input.EncodingName, input.FirstDataRow), changes, warnings, seen.Count);
    }

    public static int FindColumn(string[] headers, string name)
    {
        var indices = headers.Select((value, index) => (value, index))
            .Where(x => string.Equals(x.value, name, StringComparison.Ordinal)).Select(x => x.index).ToArray();
        if (string.IsNullOrWhiteSpace(name) || indices.Length != 1)
            throw new ArgumentException($"Колонку «{name}» не знайдено або її заголовок повторюється. Перевірте налаштування колонок.");
        return indices[0];
    }
}
