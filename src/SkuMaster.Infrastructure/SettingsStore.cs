using System.Text.Json;
using SkuMaster.Core;
namespace SkuMaster.Infrastructure;

public sealed class SettingsStore
{
    private readonly string path;
    public SettingsStore(string? path = null) => this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkuMaster", "settings.json");

    public (AppSettings Settings, string? Warning) Load()
    {
        if (!File.Exists(path)) return (new AppSettings(), null);
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Налаштування порожні.");
            Validate(settings);
            return (settings, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return (new AppSettings(), "Не вдалося прочитати налаштування або їхня версія не підтримується. Відновлено початкові значення."); }
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            FileService.AtomicWrite(fullPath, stream => JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new InvalidDataException("Не вдалося зберегти налаштування. Перевірте доступ до папки профілю.", ex); }
    }

    private static void Validate(AppSettings s)
    {
        if (s is null || s.SchemaVersion != 1 || s.OperationId != "sku-status" || s.Rules is null || s.CsvInput is null || s.OneC is null || s.Supplier is null || s.Export is null)
            throw new InvalidDataException("Версія або структура налаштувань не підтримується.");
        if (string.IsNullOrWhiteSpace(s.Rules.SkuColumn) || string.IsNullOrWhiteSpace(s.Rules.StatusColumn) || s.Rules.SkuColumn == s.Rules.StatusColumn || string.IsNullOrWhiteSpace(s.Rules.UnavailableStatus) || s.Rules.MatchStatus is null || s.Rules.ReplacementStatus is null)
            throw new InvalidDataException("Перевірте назви колонок і статус недоступності в налаштуваннях.");
        if (s.CsvInput.Delimiter != "auto") FileService.ValidateDelimiter(s.CsvInput.Delimiter);
        if (s.CsvInput.EncodingName != "auto") _ = FileService.GetEncoding(s.CsvInput.EncodingName);
        if (s.OneC.SheetName is null || s.Supplier.SheetName is null) throw new InvalidDataException("Некоректне ім’я аркуша.");
        if (s.Export.Format is not ("csv" or "xlsx")) throw new InvalidDataException("Невідомий формат експорту.");
        if (s.Export.Delimiter != "source") FileService.ValidateDelimiter(s.Export.Delimiter);
        if (s.Export.EncodingName != "source") _ = FileService.GetEncoding(s.Export.EncodingName);
        if (string.IsNullOrWhiteSpace(s.Export.SkuColumn) || string.IsNullOrWhiteSpace(s.Export.StatusColumn) || s.Export.SkuColumn == s.Export.StatusColumn)
            throw new InvalidDataException("Некоректні вхідні колонки експорту.");
        if (s.Export.IncludeHeaders && (string.IsNullOrWhiteSpace(s.Export.SkuHeader) || string.IsNullOrWhiteSpace(s.Export.StatusHeader) || s.Export.SkuHeader == s.Export.StatusHeader))
            throw new InvalidDataException("Заголовки експорту мають бути непорожніми та різними.");
    }
}

