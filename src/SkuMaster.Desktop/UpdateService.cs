using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace SkuMaster.Desktop;
public sealed class UpdateService
{
    private UpdateManager? manager;
    private UpdateInfo? pending;
    public static bool IsValidRepository(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "github.com" && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Segments.Length == 3 && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    public async Task<bool> CheckAndDownloadAsync(Action<string> status)
    {
        var repo = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "UpdateRepository")?.Value;
        if (string.IsNullOrWhiteSpace(repo)) { status("Джерело оновлень не налаштовано"); return false; }
        if (!IsValidRepository(repo)) { status("Некоректне джерело оновлень"); return false; }
        if (pending != null) { status("Оновлення готове. Натисніть «Оновлення» для встановлення."); return true; }
        manager ??= new UpdateManager(new GithubSource(repo, null, false));
        if (!manager.IsInstalled) { status("Автооновлення доступне після встановлення програми."); return false; }
        status("Перевіряємо оновлення…");
        var update = await manager.CheckForUpdatesAsync();
        if (update == null) { status("У вас остання версія"); return false; }
        status("Завантажуємо оновлення…");
        await manager.DownloadUpdatesAsync(update);
        pending = update;
        status("Оновлення готове. Натисніть «Оновлення» для встановлення.");
        return true;
    }
    public void ApplyAndRestart()
    {
        if (pending == null || manager == null) return;
        manager.ApplyUpdatesAndRestart(pending);
    }
}
