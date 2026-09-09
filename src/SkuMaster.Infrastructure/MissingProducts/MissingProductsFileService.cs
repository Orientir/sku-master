using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SkuMaster.Core.MissingProducts;

namespace SkuMaster.Infrastructure.MissingProducts;

public sealed class MissingProductsFileService
{
    public IReadOnlyList<string> ReadSite(string path, MissingFileOptions options, CancellationToken token = default) => Guard(path, () =>
    {
        if (!Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Експорт сайту має бути CSV.");
        Validate(options, false);
        var values = ReadCsv(path, options, token).Select((r, i) =>
        {
            if (r.Length < options.SkuColumn) throw new InvalidDataException($"Рядок {i + options.FirstDataRow}: колонка артикулу відсутня.");
            return r[options.SkuColumn - 1].Trim();
        }).ToArray();
        if (!values.Any(s => !string.IsNullOrWhiteSpace(s))) throw new InvalidDataException("Вибрана колонка не містить артикулів. Перевірте колонку та перший рядок даних.");
        return values;
    });

    public IReadOnlyList<MissingProduct> ReadSupplier(string path, MissingFileOptions options, CancellationToken token = default) => Guard(path, () => ReadExcel(path, options, true, token));
    public IReadOnlyList<string> ReadExclusions(string path, MissingFileOptions options, CancellationToken token = default) => Guard(path, () => ReadExcel(path, options, false, token).Select(p => p.Sku).ToArray());
    public string[] GetSheets(string path) => Guard(path, () =>
    {
        using var book = OpenBook(path, default);
        return Enumerable.Range(0, book.NumberOfSheets).Select(book.GetSheetName).ToArray();
    });

    private static void Validate(MissingFileOptions options, bool names)
    {
        if (options.SkuColumn < 1 || options.FirstDataRow < 1 || (names && options.NameColumn < 1)) throw new InvalidDataException("Номери колонок і першого рядка мають бути не менші за 1.");
        if (names && options.NameColumn == options.SkuColumn) throw new InvalidDataException("Колонки артикулу та назви мають бути різними.");
    }

    private static IReadOnlyList<MissingProduct> ReadExcel(string path, MissingFileOptions options, bool names, CancellationToken token)
    {
        Validate(options, names);
        using var book = OpenBook(path, token);
        var sheet = string.IsNullOrEmpty(options.SheetName) ? (book.NumberOfSheets > 0 ? book.GetSheetAt(0) : null) : book.GetSheet(options.SheetName);
        if (sheet is null) throw new InvalidDataException("Вибраний аркуш відсутній у файлі.");
        var formatter = new DataFormatter(CultureInfo.InvariantCulture);
        var evaluator = names ? book.GetCreationHelper().CreateFormulaEvaluator() : null;
        var result = new List<MissingProduct>();
        bool nameColumnExists = false;
        for (int i = options.FirstDataRow - 1; i <= sheet.LastRowNum; i++)
        {
            token.ThrowIfCancellationRequested();
            var row = sheet.GetRow(i);
            if (row is null) continue;
            var cell = row.GetCell(options.SkuColumn - 1);
            if (cell?.CellType is CellType.Formula or CellType.Error) throw new InvalidDataException($"Рядок {i + 1}: артикул містить формулу або помилку Excel. Замініть його текстовим значенням.");
            var sku = cell is null ? "" : cell.CellType == CellType.Numeric && (cell.CellStyle.DataFormat == 0 || cell.CellStyle.GetDataFormatString() == "General")
                ? cell.NumericCellValue.ToString("0.###############################", CultureInfo.InvariantCulture)
                : formatter.FormatCellValue(cell).Trim();
            var nameCell = names ? row.GetCell(options.NameColumn - 1) : null;
            if (nameCell is not null) nameColumnExists = true;
            if (nameCell?.CellType == CellType.Error || (nameCell?.CellType == CellType.Formula && evaluator!.Evaluate(nameCell).CellType == CellType.Error))
                throw new InvalidDataException($"Рядок {i + 1}: назва містить помилку Excel.");
            var name = names ? formatter.FormatCellValue(nameCell, evaluator) : "";
            result.Add(new(sku, name));
        }
        if (!result.Any(p => p.Sku.Length > 0)) throw new InvalidDataException("Вибрана колонка не містить артикулів. Перевірте аркуш, колонку та перший рядок даних. Список винятків не змінено.");
        if (names && !nameColumnExists) throw new InvalidDataException("Вибрана колонка назв відсутня у рядках даних. Перевірте номер колонки.");
        return result;
    }

    private static byte[] Snapshot(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var source = new SharedInputFile(path);
        long size = source.Stream.Length;
        var modified = File.GetLastWriteTimeUtc(path);
        using var target = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = source.Stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); target.Write(buffer, 0, read); }
        token.ThrowIfCancellationRequested();
        if (source.Stream.Length != size || target.Length != size || modified != File.GetLastWriteTimeUtc(path)) throw new InvalidDataException("Файл змінився під час читання. Повторіть спробу після збереження файлу.");
        return target.ToArray();
    }

    private static IWorkbook OpenBook(string path, CancellationToken token)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".xls" or ".xlsx")) throw new InvalidDataException("Оберіть файл Excel XLS або XLSX.");
        using var stream = new MemoryStream(Snapshot(path, token), false);
        return WorkbookFactory.Create(stream);
    }

    private static List<string[]> ReadCsv(string path, MissingFileOptions options, CancellationToken token)
    {
        var bytes = Snapshot(path, token);
        bool bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
        var encoding = FileService.GetEncoding(options.EncodingName == "auto" ? "utf-8" : options.EncodingName);
        string contents;
        try { contents = encoding.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
        catch (DecoderFallbackException) when (options.EncodingName == "auto") { contents = FileService.GetEncoding("windows-1251").GetString(bytes); }
        if (contents.Contains('\0')) throw new InvalidDataException("CSV містить нульові символи. Перевірте кодування.");
        List<string[]> Parse(string delimiter)
        {
            using var reader = new StringReader(contents);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false, Delimiter = delimiter, IgnoreBlankLines = false });
            var rows = new List<string[]>();
            while (csv.Read()) { token.ThrowIfCancellationRequested(); rows.Add(csv.Parser.Record!.ToArray()); }
            return rows;
        }
        List<string[]> parsed;
        if (options.Delimiter == "auto")
        {
            var candidates = new List<List<string[]>>();
            List<string[]>? single = null;
            foreach (var delimiter in new[] { ";", ",", "\t" })
            {
                try { var rows = Parse(delimiter); if (rows.Skip(options.FirstDataRow - 1).Any(r => r.Length > 1)) candidates.Add(rows); else single ??= rows; }
                catch (CsvHelperException) { }
            }
            if (candidates.Count > 1) throw new InvalidDataException("Роздільник CSV неоднозначний. Виберіть його вручну.");
            parsed = candidates.Count == 1 ? candidates[0] : single ?? throw new InvalidDataException("Не вдалося прочитати CSV. Перевірте лапки та роздільник.");
        }
        else { FileService.ValidateDelimiter(options.Delimiter); parsed = Parse(options.Delimiter); }
        var data = parsed.Skip(options.FirstDataRow - 1).ToList();
        var nonblank = data.Where(r => r.Any(s => !string.IsNullOrWhiteSpace(s))).ToArray();
        if (nonblank.Select(r => r.Length).Distinct().Count() > 1) throw new InvalidDataException("Рядки CSV містять різну кількість колонок. Перевірте структуру файлу та роздільник.");
        return data;
    }

    public void Export(IReadOnlyList<MissingProduct> products, string path, string format, IReadOnlyList<string> protectedPaths, CancellationToken token = default)
        => ExportTable(products, path, format, protectedPaths, false, token);
    public void ExportExclusions(IReadOnlyList<string> skus, string path, string format, IReadOnlyList<string> protectedPaths, CancellationToken token = default)
        => ExportTable(skus.Select(sku => new MissingProduct(sku, "")).ToArray(), path, format, protectedPaths, true, token);
    private void ExportTable(IReadOnlyList<MissingProduct> products, string path, string format, IReadOnlyList<string> protectedPaths, bool exclusionsOnly, CancellationToken token) => Guard(path, () =>
    {
        var fullPath = Path.GetFullPath(path);
        if (protectedPaths.Any(p => !string.IsNullOrWhiteSpace(p) && string.Equals(Path.GetFullPath(p), fullPath, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Не можна перезаписувати вихідний файл. Оберіть інше ім’я результату.");
        if (format is not ("csv" or "xlsx")) throw new InvalidDataException("Оберіть формат CSV або XLSX.");
        if (!Path.GetExtension(fullPath).Equals("." + format, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Розширення файлу має відповідати вибраному формату CSV або XLSX.");
        FileService.AtomicWrite(fullPath, stream =>
        {
            if (format == "csv")
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
                using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";", NewLine = "\r\n" }, leaveOpen: true);
                void Row(string sku, string name) { token.ThrowIfCancellationRequested(); csv.WriteField(sku); if (!exclusionsOnly) csv.WriteField(name); csv.NextRecord(); }
                Row("Артикул", "Назва");
                foreach (var product in products) Row(product.Sku, product.Name);
            }
            else
            {
                if (products.Count > 1048575) throw new InvalidDataException("Забагато рядків для XLSX. Оберіть CSV.");
                using var book = new XSSFWorkbook();
                var sheet = book.CreateSheet(exclusionsOnly ? "Виключення" : "Відсутні товари");
                int index = 0;
                void Row(string sku, string name)
                {
                    token.ThrowIfCancellationRequested();
                    if (sku.Length > 32767 || name.Length > 32767) throw new InvalidDataException("Текст клітинки перевищує обмеження XLSX. Оберіть CSV.");
                    var row = sheet.CreateRow(index++);
                    row.CreateCell(0, CellType.String).SetCellValue(sku);
                    if (!exclusionsOnly) row.CreateCell(1, CellType.String).SetCellValue(name);
                }
                Row("Артикул", "Назва");
                foreach (var product in products) Row(product.Sku, product.Name);
                book.Write(stream, true);
            }
        }, token);
        return true;
    });

    private static T Guard<T>(string path, Func<T> read)
    {
        try { return read(); }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new InvalidDataException($"Не вдалося обробити файл «{Path.GetFileName(path)}». Перевірте формат, кодування та доступ до файлу.", ex); }
    }
}
