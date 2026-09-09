using System.Text.Json;
using SkuMaster.Core.MissingProducts;

namespace SkuMaster.Infrastructure.MissingProducts;

public sealed class ExclusionStore(string? path = null)
{
    private readonly string file = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkuMaster", "missing-products", "exclusions.json"));
    public string FilePath => file;
    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(file)) return [];
        try
        {
            var data = JsonSerializer.Deserialize<string[]>(File.ReadAllText(file));
            if (data is null || data.Any(s => s is null)) throw new JsonException();
            return Normalize(data);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { throw new InvalidDataException("Не вдалося завантажити список винятків. Файл пошкоджено або недоступний; наявні дані не змінено.", ex); }
    }
    public void Save(IEnumerable<string> values)
    {
        var data = Normalize(values);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        FileService.AtomicWrite(file, stream => JsonSerializer.Serialize(stream, data));
    }
    private static string[] Normalize(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToArray();
}

public sealed class MissingModuleSettingsStore(string? path = null)
{
    private readonly string file = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkuMaster", "missing-products", "settings.json"));
    public string FilePath => file;
    public MissingModuleSettings Load()
    {
        if (!File.Exists(file)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<MissingModuleSettings>(File.ReadAllText(file));
            if (settings is null || settings.Site is null || settings.Supplier is null || settings.Exclusions is null) throw new JsonException();
            Validate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new InvalidDataException("Не вдалося завантажити налаштування пошуку відсутніх товарів: файл пошкоджено або недоступний.", ex); }
    }
    public void Save(MissingModuleSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        FileService.AtomicWrite(file, stream => JsonSerializer.Serialize(stream, settings));
    }
    private static void Validate(MissingModuleSettings settings)
    {
        if (settings.OutputFormat is not ("csv" or "xlsx")) throw new ArgumentException("Невідомий формат результату.");
        foreach (var o in new[] { settings.Site, settings.Supplier, settings.Exclusions })
            if (o is null || o.SkuColumn < 1 || o.NameColumn < 1 || o.FirstDataRow < 1 || o.SheetName is null || o.Delimiter is not ("auto" or ";" or "," or "\t") || o.EncodingName is not ("auto" or "utf-8" or "utf-8-bom" or "windows-1251"))
                throw new ArgumentException("Некоректні параметри колонок, рядків або кодування.");
    }
}
