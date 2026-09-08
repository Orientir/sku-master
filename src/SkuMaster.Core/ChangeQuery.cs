namespace SkuMaster.Core;

public static class ChangeQuery
{
    public static IReadOnlyList<StatusChange> Apply(IReadOnlyList<StatusChange> changes, string search,
        int kind = 0, string? oldStatus = null, string? newStatus = null)
    {
        var query = search.Trim();
        return changes.Where(row =>
            ((kind == 0 && row.OldStatus != row.NewStatus)
                || (kind == 1 && row.Kind == ChangeKind.Unavailable)
                || (kind == 2 && row.Kind == ChangeKind.Available)
                || kind == 3 || (kind == 4 && row.OldStatus == row.NewStatus))
            && (oldStatus == null || row.OldStatus == oldStatus)
            && (newStatus == null || row.NewStatus == newStatus)
            && (query.Length == 0 || row.Sku.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.OldStatus.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.NewStatus.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
