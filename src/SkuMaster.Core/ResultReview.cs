namespace SkuMaster.Core;

public sealed class ResultReview
{
    private readonly int statusIndex;
    private readonly string unavailableStatus;
    private readonly StatusChange[] automaticRows;
    private readonly StatusChange[] rows;
    public ResultReview(OperationResult result, string skuColumn, string statusColumn, string unavailableStatus)
    {
        statusIndex = StatusOperation.FindColumn(result.Output.Headers, statusColumn);
        var skuIndex = StatusOperation.FindColumn(result.Output.Headers, skuColumn);
        this.unavailableStatus = unavailableStatus;
        var changes = result.Changes.ToDictionary(x => x.RowNumber);
        rows = result.Output.Rows.Select((row, i) => changes.GetValueOrDefault(i + result.Output.FirstDataRow)
            ?? new StatusChange(i + result.Output.FirstDataRow, row[skuIndex], row[statusIndex], row[statusIndex], ChangeKind.Unchanged))
            .Select(row => result.SourcesBySku is null ? row : row with { Source = result.SourcesBySku.GetValueOrDefault(row.Sku.Trim()) }).ToArray();
        automaticRows = (StatusChange[])rows.Clone();
        Result = result with
        {
            Output = result.Output with { Rows = result.Output.Rows.Select(x => (string[])x.Clone()).ToArray() },
            Changes = rows.Where(x => x.OldStatus != x.NewStatus).ToArray()
        };
    }
    public IReadOnlyList<StatusChange> Rows => rows;
    public OperationResult Result { get; private set; }
    public bool SetStatus(int rowNumber, string status)
    {
        StatusCatalog.Validate(status);
        var index = rowNumber - Result.Output.FirstDataRow;
        if (index < 0 || index >= rows.Length) throw new ArgumentOutOfRangeException(nameof(rowNumber));
        var row = rows[index];
        if (row.NewStatus == status) return false;
        var automatic = automaticRows[index];
        var kind = status == row.OldStatus ? ChangeKind.Unchanged
            : status == automatic.NewStatus ? automatic.Kind
            : status == unavailableStatus ? ChangeKind.Unavailable
            : row.OldStatus == unavailableStatus ? ChangeKind.Available : ChangeKind.Manual;
        rows[index] = row with { NewStatus = status, Kind = kind, IsManual = status != automatic.NewStatus };
        // Copy the edited output row so any previously exported snapshot remains stable.
        var outputRows = Result.Output.Rows.ToArray();
        outputRows[index] = (string[])outputRows[index].Clone();
        outputRows[index][statusIndex] = status;
        Result = Result with { Output = Result.Output with { Rows = outputRows }, Changes = rows.Where(x => x.OldStatus != x.NewStatus).ToArray() };
        return true;
    }
}
