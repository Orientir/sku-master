using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SkuMaster.Core;

namespace SkuMaster.Infrastructure;

public sealed class FileService
{
    static FileService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public TableData ReadSite(string path, CsvInputSettings settings, CancellationToken cancellationToken = default) => Guard(path, () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Експорт сайту має бути файлом CSV.");
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
        var encodingName = settings.EncodingName == "auto" ? (hasBom ? "utf-8-bom" : "utf-8") : settings.EncodingName;
        var encoding = GetEncoding(encodingName);
        string contents;
        try { contents = encoding.GetString(bytes, hasBom && encodingName.StartsWith("utf-8", StringComparison.Ordinal) ? 3 : 0,
            bytes.Length - (hasBom && encodingName.StartsWith("utf-8", StringComparison.Ordinal) ? 3 : 0)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Не вдалося прочитати кодування CSV. Виберіть правильне кодування вручну (UTF-8 або Windows-1251).", ex); }
        if (contents.Contains('\0')) throw new InvalidDataException("CSV містить нульові символи. Перевірте кодування файлу.");
        string delimiter = settings.Delimiter;
        List<string[]> rows;
        if (delimiter == "auto")
        {
            var candidates = new List<(string Delimiter, List<string[]> Rows)>();
            foreach (var candidate in new[] { ";", ",", "\t" })
            {
                try
                {
                    var parsed = ParseCsv(contents, candidate[0], cancellationToken);
                    if (parsed.Count > 0 && parsed[0].Length > 1) candidates.Add((candidate, parsed));
                }
                catch (InvalidDataException) { }
            }
            if (candidates.Count != 1) throw new InvalidDataException("Не вдалося однозначно визначити роздільник CSV або структура CSV пошкоджена. Виберіть роздільник вручну та перевірте файл.");
            (delimiter, rows) = candidates[0];
        }
        else
        {
            ValidateDelimiter(delimiter);
            rows = ParseCsv(contents, delimiter[0], cancellationToken);
        }
        if (rows.Count == 0) throw new InvalidDataException("CSV порожній: немає рядка заголовків.");
        return new TableData(rows[0], rows.Skip(1).ToArray(), delimiter, encodingName);
    });

    // A strict state machine rejects unclosed quotes, quotes inside unquoted fields,
    // characters after closing quotes, and inconsistent record widths.
    private static List<string[]> ParseCsv(string text, char delimiter, CancellationToken token)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closed = false, recordStarted = false;
        int line = 1;
        void EndField() { fields.Add(field.ToString()); field.Clear(); closed = false; }
        void EndRow()
        {
            if (recordStarted || fields.Count > 0 || field.Length > 0)
            {
                EndField();
                if (rows.Count > 0 && fields.Count != rows[0].Length)
                    throw new InvalidDataException($"Рядок CSV біля {line}: кількість колонок не збігається із заголовками.");
                rows.Add(fields.ToArray());
            }
            fields.Clear(); field.Clear(); closed = false; recordStarted = false;
        }
        for (int i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { field.Append(ch); if (ch == '\n') line++; }
                continue;
            }
            if (ch == delimiter) { EndField(); recordStarted = true; }
            else if (ch is '\r' or '\n') { EndRow(); if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; line++; }
            else if (closed) throw new InvalidDataException($"Рядок CSV {line}: зайві символи після закриття лапок.");
            else if (ch == '"')
            {
                if (field.Length != 0) throw new InvalidDataException($"Рядок CSV {line}: лапки всередині неекранованого поля.");
                quoted = true; recordStarted = true;
            }
            else { field.Append(ch); recordStarted = true; }
        }
        if (quoted) throw new InvalidDataException($"Рядок CSV {line}: незакриті лапки.");
        EndRow(); token.ThrowIfCancellationRequested(); return rows;
    }

    public SourceData ReadSource(string path, SourceSettings settings, CancellationToken cancellationToken = default) => Guard(path, () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var book = OpenWorkbook(path);
        var sheet = string.IsNullOrEmpty(settings.SheetName) ? (book.NumberOfSheets > 0 ? book.GetSheetAt(0) : null) : book.GetSheet(settings.SheetName);
        if (sheet is null) throw new InvalidDataException("Вибраний аркуш відсутній у файлі. Виберіть аркуш повторно.");
        var skus = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var formatter = new DataFormatter(CultureInfo.InvariantCulture);
        for (int i = settings.HasHeader ? 1 : 0; i <= sheet.LastRowNum; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = sheet.GetRow(i);
            if (row is null || row.Cells.All(c => c.CellType == CellType.Blank || (c.CellType == CellType.String && string.IsNullOrWhiteSpace(c.StringCellValue)))) continue;
            var cell = row.GetCell(0);
            if (cell?.CellType == CellType.Formula) throw new InvalidDataException($"Рядок {i + 1}: SKU містить формулу. Замініть формулу текстовим значенням.");
            if (cell?.CellType == CellType.Error) throw new InvalidDataException($"Рядок {i + 1}: клітинка SKU містить помилку Excel.");
            var sku = cell is null ? "" : cell.CellType == CellType.Numeric
                && (cell.CellStyle.DataFormat == 0 || cell.CellStyle.GetDataFormatString() == "General")
                ? cell.NumericCellValue.ToString("0.###############################", CultureInfo.InvariantCulture)
                : formatter.FormatCellValue(cell).Trim();
            if (sku.Length == 0) warnings.Add($"{Path.GetFileName(path)}, рядок {i + 1}: порожній SKU, рядок пропущено.");
            else if (!skus.Add(sku)) warnings.Add($"{Path.GetFileName(path)}, рядок {i + 1}: повторний SKU «{sku}».");
        }
        return new SourceData(skus, warnings);
    });

    public string[] GetSheets(string path) => Guard(path, () =>
    {
        using var book = OpenWorkbook(path);
        return Enumerable.Range(0, book.NumberOfSheets).Select(book.GetSheetName).ToArray();
    });

    private static IWorkbook OpenWorkbook(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".xls" or ".xlsx"))
            throw new InvalidDataException("Джерело має бути файлом XLS або XLSX.");
        using var stream = File.OpenRead(path);
        return WorkbookFactory.Create(stream);
    }

    public void Export(TableData table, string path, ExportSettings settings, IReadOnlyList<string> sourcePaths, CancellationToken cancellationToken = default) => Guard(path, () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        if (sourcePaths.Any(p => !string.IsNullOrWhiteSpace(p) && string.Equals(Path.GetFullPath(p), fullPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Не можна перезаписувати вихідний файл. Виберіть інше ім’я результату.");
        if (settings.Format is not ("csv" or "xlsx")) throw new InvalidDataException("Виберіть формат експорту CSV або XLSX.");
        if (table.Rows.Any(row => row.Length != table.Headers.Length)) throw new InvalidDataException("Кількість колонок у результаті не збігається із заголовками.");
        var headers = (string[])table.Headers.Clone();
        if (settings.IncludeHeaders)
        {
            int Find(string name)
            {
                var indexes = Enumerable.Range(0, headers.Length).Where(i => headers[i] == name).ToArray();
                if (indexes.Length != 1) throw new InvalidDataException($"Не вдалося однозначно знайти колонку «{name}» для експорту.");
                return indexes[0];
            }
            int sku = Find(settings.SkuColumn), status = Find(settings.StatusColumn);
            if (sku == status) throw new InvalidDataException("Колонки SKU та статусу мають бути різними.");
            headers[sku] = settings.SkuHeader; headers[status] = settings.StatusHeader;
            if (headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
                throw new InvalidDataException("Заголовки експорту не можуть бути порожніми або повторюватися.");
        }
        AtomicWrite(fullPath, stream =>
        {
            if (settings.Format == "csv")
            {
                string delimiter = settings.Delimiter == "source" ? table.Delimiter : settings.Delimiter;
                ValidateDelimiter(delimiter);
                var encoding = GetEncoding(settings.EncodingName == "source" ? table.EncodingName : settings.EncodingName);
                try
                {
                    using var writer = new StreamWriter(stream, encoding, 4096, leaveOpen: true);
                    using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = delimiter, NewLine = "\r\n" }, leaveOpen: true);
                    void WriteRow(string[] row) { cancellationToken.ThrowIfCancellationRequested(); foreach (var value in row) csv.WriteField(value); csv.NextRecord(); }
                    if (settings.IncludeHeaders) WriteRow(headers);
                    foreach (var row in table.Rows) WriteRow(row);
                    csv.Flush(); writer.Flush();
                }
                catch (EncoderFallbackException ex) { throw new InvalidDataException("Деякі символи неможливо зберегти у вибраному кодуванні. Виберіть UTF-8.", ex); }
            }
            else
            {
                if (table.Headers.Length > 16384 || table.Rows.Count + (settings.IncludeHeaders ? 1 : 0) > 1048576)
                    throw new InvalidDataException("Результат перевищує обмеження XLSX. Виберіть CSV.");
                using var book = new XSSFWorkbook();
                var sheet = book.CreateSheet("Результат");
                int index = 0;
                void WriteRow(string[] values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = sheet.CreateRow(index++);
                    for (int col = 0; col < values.Length; col++)
                    {
                        if (values[col].Length > 32767) throw new InvalidDataException("Текст клітинки перевищує обмеження XLSX (32767 символів). Виберіть CSV.");
                        row.CreateCell(col, CellType.String).SetCellValue(values[col]);
                    }
                }
                if (settings.IncludeHeaders) WriteRow(headers);
                foreach (var row in table.Rows) WriteRow(row);
                book.Write(stream, true);
            }
        }, cancellationToken);
        return true;
    });

    internal static void AtomicWrite(string path, Action<FileStream> write, CancellationToken token = default)
    {
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(stream); stream.Flush(true); }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static Encoding GetEncoding(string name) => name switch
    {
        "utf-8" => new UTF8Encoding(false, true),
        "utf-8-bom" => new UTF8Encoding(true, true),
        "windows-1251" => Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new InvalidDataException("Невідоме кодування. Виберіть UTF-8 або Windows-1251.")
    };

    internal static void ValidateDelimiter(string value)
    {
        if (value is not (";" or "," or "\t")) throw new InvalidDataException("Виберіть роздільник: кома, крапка з комою або табуляція.");
    }

    private static T Guard<T>(string path, Func<T> action)
    {
        try { return action(); }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException ex) { throw new InvalidDataException($"{Path.GetFileName(path)}: {ex.Message}", ex); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new InvalidDataException($"Не вдалося обробити файл «{Path.GetFileName(path)}». Перевірте формат, доступ до файлу та вільне місце на диску.", ex); }
    }
}
