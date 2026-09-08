namespace SkuMaster.Core;

public sealed record TableData(string[] Headers, IReadOnlyList<string[]> Rows,
    string Delimiter = ";", string EncodingName = "utf-8-bom", int FirstDataRow = 2);

public sealed class RuleSettings
{
    public string SkuColumn { get; set; } = "sku";
    public string StatusColumn { get; set; } = "status";
    public string UnavailableStatus { get; set; } = "Временно недоступен";
    public string MatchStatus { get; set; } = "Временно недоступен";
    public bool ClearFoundStatus { get; set; } = true;
    public string ReplacementStatus { get; set; } = "Новинка";
}

public sealed class CsvInputSettings
{
    public bool HasHeader { get; set; }
    public string Delimiter { get; set; } = "auto";
    public string EncodingName { get; set; } = "auto";
}

public sealed class SourceSettings
{
    public bool HasHeader { get; set; }
    public string SheetName { get; set; } = "";
}

public sealed class ExportSettings
{
    public bool OnlyChanged { get; set; } = true;
    public string Format { get; set; } = "csv";
    public bool IncludeHeaders { get; set; } = true;
    public string SkuHeader { get; set; } = "sku";
    public string StatusHeader { get; set; } = "status";
    public string Delimiter { get; set; } = "source";
    public string EncodingName { get; set; } = "source";
    public string SkuColumn { get; set; } = "sku";
    public string StatusColumn { get; set; } = "status";
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
    public string OperationId { get; set; } = "sku-status";
    public RuleSettings Rules { get; set; } = new();
    public CsvInputSettings CsvInput { get; set; } = new();
    public SourceSettings OneC { get; set; } = new();
    public SourceSettings Supplier { get; set; } = new();
    public ExportSettings Export { get; set; } = new();
}

public sealed record SourceData(IReadOnlySet<string> Skus, IReadOnlyList<string> Warnings);
public enum ChangeKind { Unavailable, Available, Unchanged, Manual }
[Flags]
public enum AvailabilitySource { None = 0, OneC = 1, Supplier = 2 }
public sealed record StatusChange(int RowNumber, string Sku, string OldStatus, string NewStatus, ChangeKind Kind)
{
    public string DisplayNewStatus => NewStatus.Length == 0 ? "(порожньо)" : NewStatus;
    public string DisplayOldStatus => OldStatus.Length == 0 ? "(порожньо)" : OldStatus;
    public bool IsManual { get; init; }
    public AvailabilitySource? Source { get; init; }
    public string Reason => Source is { } source
        ? (IsManual ? "Змінено вручну · " : Kind == ChangeKind.Unchanged ? "Без змін · " : "") +
            (string.IsNullOrWhiteSpace(Sku) ? "Порожній SKU" : source switch
            {
                AvailabilitySource.OneC => "Знайдено в 1С",
                AvailabilitySource.Supplier => "Знайдено у постачальника",
                AvailabilitySource.OneC | AvailabilitySource.Supplier => "Знайдено в 1С та у постачальника",
                _ => "Немає в 1С та у постачальника"
            })
        : IsManual ? "Змінено вручну" : Kind switch
    {
        ChangeKind.Unavailable => "Немає в 1С та у постачальника",
        ChangeKind.Available => "Знайдено в 1С або у постачальника",
        ChangeKind.Unchanged => "Без змін",
        _ => "Змінено вручну"
    };
}
public sealed record OperationResult(TableData Output, IReadOnlyList<StatusChange> Changes,
    IReadOnlyList<string> Warnings, int UniqueSkus)
{
    public IReadOnlyDictionary<string, AvailabilitySource>? SourcesBySku { get; init; }
    public int Total => Output.Rows.Count;
    public int Changed => Changes.Count;
    public int Unavailable => Changes.Count(x => x.Kind == ChangeKind.Unavailable);
    public int Available => Changes.Count(x => x.Kind == ChangeKind.Available);
    public int Unchanged => Total - Changed;
    public TableData GetExportTable(bool onlyChanged)
    {
        if (!onlyChanged) return Output;
        var changedRows = Changes.Select(x => x.RowNumber).ToHashSet();
        return Output with { Rows = Output.Rows.Where((_, index) => changedRows.Contains(index + Output.FirstDataRow)).ToArray() };
    }
}
public interface ITableOperation
{
    string Id { get; }
    string DisplayName { get; }
    OperationResult Execute(TableData input, IReadOnlySet<string> available, RuleSettings settings,
        CancellationToken cancellationToken = default);
}
