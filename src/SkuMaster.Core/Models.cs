namespace SkuMaster.Core;

public sealed record TableData(string[] Headers, IReadOnlyList<string[]> Rows,
    string Delimiter = ";", string EncodingName = "utf-8-bom");

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
    public int SchemaVersion { get; set; } = 1;
    public string OperationId { get; set; } = "sku-status";
    public RuleSettings Rules { get; set; } = new();
    public CsvInputSettings CsvInput { get; set; } = new();
    public SourceSettings OneC { get; set; } = new();
    public SourceSettings Supplier { get; set; } = new();
    public ExportSettings Export { get; set; } = new();
}

public sealed record SourceData(IReadOnlySet<string> Skus, IReadOnlyList<string> Warnings);
public enum ChangeKind { Unavailable, Available }
public sealed record StatusChange(int RowNumber, string Sku, string OldStatus, string NewStatus, ChangeKind Kind)
{
    public string DisplayNewStatus => NewStatus.Length == 0 ? "(порожньо)" : NewStatus;
    public string DisplayOldStatus => OldStatus.Length == 0 ? "(порожньо)" : OldStatus;
    public string Reason => Kind == ChangeKind.Unavailable ? "Немає в 1С та у постачальника" : "Знайдено в 1С або у постачальника";
}
public sealed record OperationResult(TableData Output, IReadOnlyList<StatusChange> Changes,
    IReadOnlyList<string> Warnings, int UniqueSkus)
{
    public int Total => Output.Rows.Count;
    public int Changed => Changes.Count;
    public int Unavailable => Changes.Count(x => x.Kind == ChangeKind.Unavailable);
    public int Available => Changes.Count(x => x.Kind == ChangeKind.Available);
    public int Unchanged => Total - Changed;
}
public interface ITableOperation
{
    string Id { get; }
    string DisplayName { get; }
    OperationResult Execute(TableData input, IReadOnlySet<string> available, RuleSettings settings,
        CancellationToken cancellationToken = default);
}
