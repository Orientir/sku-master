using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SkuMaster.Desktop.Web;
using Xunit;

namespace SkuMaster.Desktop.Tests;

[CollectionDefinition("Desktop UI", DisableParallelization = true)]
public sealed class DesktopUiCollection { }

[Collection("Desktop UI")]
public sealed class WebViewIntegrationTests
{
    [Fact]
    public void BundledUiLoadsOfflineAndProcessesRealBridgeMessages() => OnSta(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("sku-webview-").FullName;
        var bridge = DesktopBridgeTests.CreateBridge(dir);
        var window = new WebMainWindow(bridge, Path.Combine(dir, "profile"));
        window.Show();
        var browser = (WebView2)window.FindName("Browser");
        try
        {
            await Until(async () => browser.CoreWebView2 != null && (await browser.CoreWebView2.ExecuteScriptAsync("document.body?.innerText || ''")).Contains("Оновлення статусів"));
            var core = browser.CoreWebView2;
            Assert.StartsWith("https://sku-master.local/", core.Source);
            await core.ExecuteScriptAsync("window.__testReplies={};window.chrome.webview.addEventListener('message',e=>{if(e.data.id)window.__testReplies[e.data.id]=e.data;});window.__uiErrors=[];window.addEventListener('error',e=>window.__uiErrors.push(e.message));window.addEventListener('unhandledrejection',e=>window.__uiErrors.push(String(e.reason)));");
            long id = 1000;
            async Task<JsonElement> Request(string module, string action, object? data = null)
            {
                var requestId = ++id;
                var json = JsonSerializer.Serialize(new { id = requestId, module, action, data = data ?? new { } }, DesktopBridge.Json);
                await core.ExecuteScriptAsync("window.chrome.webview.postMessage(" + json + ")");
                await Until(async () => await core.ExecuteScriptAsync($"Boolean(window.__testReplies[{requestId}])") == "true");
                return JsonDocument.Parse(await core.ExecuteScriptAsync($"window.__testReplies[{requestId}]" )).RootElement.Clone();
            }
            // Exercise the same message protocol as the React buttons, with actual local CSV/XLSX data.
            var reply = await Request("status", "analyze");
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            Assert.True(bridge.Status.HasResult);
            reply = await Request("status", "edit", new { row = 3, value = "" });
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            reply = await Request("status", "save", new { folder = dir, fileName = "native-result" });
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            Assert.True(File.Exists(Path.Combine(dir, "native-result.csv")));
            reply = await Request("missing", "analyze");
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            reply = await Request("missing", "exclude", new { sku = "002", value = true });
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            Assert.Contains("002", bridge.Missing.SavedExclusions);
            reply = await Request("app", "copy", new { text = "001" });
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            Assert.Equal("001", Clipboard.GetText());

            var artifacts = FindArtifacts();
            async Task Capture(string name)
            {
                await Task.Delay(150);
                using var file = File.Create(Path.Combine(artifacts, name + ".png"));
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            }
            async Task Tab(string value)
            {
                await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=tab]')].find(e=>e.offsetParent!==null && e.textContent.trim().startsWith(" + JsonSerializer.Serialize(value) + "))?.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,button:0,ctrlKey:false}))");
                await Task.Delay(100);
            }
            await Capture("web-status-files");
            await Tab("Налаштування"); await Capture("web-status-settings");
            await Tab("Результат"); await Capture("web-status-results");
            await Until(async () => (await core.ExecuteScriptAsync("document.body.innerText")).Contains("Знайдено в 1С"));
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.textContent.startsWith('Знову доступні')).click()");
            await Until(async () => await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=tabpanel] tbody tr')].filter(e=>e.offsetParent!==null).length") == "2");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Скинути').click()");
            await Until(async () => await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=tabpanel] tbody tr')].filter(e=>e.offsetParent!==null).length") == "4");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.textContent.includes('Зберегти результат')).click()");
            await Until(async () => await core.ExecuteScriptAsync("Boolean(document.querySelector('[role=dialog]'))") == "true");
            await Capture("web-save-dialog");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent==='Скасувати').click()");
            Assert.Equal("[]", await core.ExecuteScriptAsync("window.__uiErrors"));
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.textContent==='Товари, яких немає на сайті').click()");
            await Tab("Налаштування"); await Capture("web-missing-settings");
            await Tab("Як користуватися"); await Capture("web-missing-help");
            Assert.Contains("Новий імпорт повністю замінює", await core.ExecuteScriptAsync("document.body.innerText"));
            await Tab("Результат");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.textContent.startsWith('Можна додати на сайт')).click()");
            await Until(async () => await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=tabpanel] tbody tr')].filter(e=>e.offsetParent!==null).length") == "2");
            await core.ExecuteScriptAsync("document.querySelector('[role=tabpanel] tbody button[role=checkbox]').click()");
            await Until(() => Task.FromResult(bridge.Missing.SavedExclusions.Contains("003")));
            Assert.Equal("2", await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=tabpanel] tbody tr')].filter(e=>e.offsetParent!==null).length"));
            await Capture("web-missing-results");
            await core.ExecuteScriptAsync("document.querySelector('[role=tabpanel] tbody button[role=checkbox]').click()");
            await Until(() => Task.FromResult(!bridge.Missing.SavedExclusions.Contains("003")));
            await Tab("Виключення"); await Capture("web-exclusions");
            var largeExclusions = Path.Combine(dir, "large-exclusions.xlsx");
            using (var book = new NPOI.XSSF.UserModel.XSSFWorkbook())
            {
                var sheet = book.CreateSheet("SKU");
                for (int i = 0; i < 26934; i++) sheet.CreateRow(i).CreateCell(0).SetCellValue($"EX{i:D5}");
                using var file = File.Create(largeExclusions); book.Write(file, true);
            }
            await bridge.PickAsync("missing", "exceptions", largeExclusions);
            await Until(async () => await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=tabpanel] li')].filter(e=>e.offsetParent!==null).length") == "200");
            await Capture("web-large-exclusions");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.offsetParent!==null && e.textContent==='Далі').click()");
            await Until(async () => (await core.ExecuteScriptAsync("document.body.innerText")).Contains("EX00200"));
            await core.ExecuteScriptAsync("var input=document.querySelector('input[placeholder=\"Пошук за артикулом…\"]');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'EX26933');input.dispatchEvent(new Event('input',{bubbles:true}));");
            await Until(async () => await core.ExecuteScriptAsync("[...document.querySelectorAll('[role=tabpanel] li')].filter(e=>e.offsetParent!==null).length") == "1");
            Assert.Contains("EX26933", await core.ExecuteScriptAsync("document.body.innerText"));
            await bridge.PickAsync("availability", "site", Path.Combine(dir, "site.csv"));
            await bridge.PickAsync("availability", "supplier", Path.Combine(dir, "supplier.xlsx"));
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.textContent==='Перевірка наявності').click()");
            await Until(async () => await core.ExecuteScriptAsync("[...document.querySelectorAll('[data-testid=availability-module] button')].find(e=>e.textContent.trim()==='Перевірити файли')?.disabled === false") == "true");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('[data-testid=availability-module] button')].find(e=>e.textContent.trim()==='Перевірити файли').click()");
            await Until(() => Task.FromResult(bridge.Availability.HasResult));
            await Until(async () => await core.ExecuteScriptAsync("document.querySelectorAll('[data-testid=availability-module] tbody tr').length") == "4");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('[data-testid=availability-module] button')].find(e=>e.textContent.startsWith('Немає у постачальника')).click()");
            await Until(async () => await core.ExecuteScriptAsync("document.querySelectorAll('[data-testid=availability-module] tbody tr').length") == "3");
            await core.ExecuteScriptAsync("document.querySelector('[data-testid=availability-module] thead input').click()");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('[data-testid=availability-module] button')].find(e=>e.textContent==='Призначити обраним').click()");
            await Until(() => Task.FromResult(bridge.Availability.Rows.Count(r=>r.Changed)==3));
            Assert.All(bridge.Availability.Rows.Where(r=>!r.Available), r=>Assert.Equal("Снят с производства",r.Next));
            reply = await Request("availability", "save", new { folder = dir, fileName = "availability-native", rows = new[] { 1,2,3,4 } });
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            Assert.Contains("Снят с производства", File.ReadAllText(Path.Combine(dir,"availability-native.csv")));
            await Capture("web-availability-results");
            // Scope by original website status, then select across pages (not merely rendered rows).
            var largeSite = Path.Combine(dir, "availability-large.csv");
            File.WriteAllLines(largeSite, Enumerable.Range(0,205).Select(i=>$"A{i:D4};Снят с производства").Append("other;Новинка"));
            await bridge.PickAsync("availability", "site", largeSite);
            reply = await Request("availability", "analyze");
            Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
            await Until(async () => await core.ExecuteScriptAsync("document.querySelectorAll('[data-testid=availability-module] tbody tr').length") == "200");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('[data-testid=availability-module] label')].find(e=>e.textContent.includes('Усі товари з файлу')).querySelector('input').click()");
            await Until(async () => (await core.ExecuteScriptAsync("document.querySelector('[data-testid=availability-module]').innerText")).Contains("Показано 205"));
            await core.ExecuteScriptAsync("document.querySelector('[data-testid=availability-module] thead input').click()");
            await Until(async () => (await core.ExecuteScriptAsync("document.querySelector('[data-testid=availability-module]').innerText")).Contains("Обрано: 205"));
            await core.ExecuteScriptAsync("var bulk=document.querySelector('select[aria-label=\"Масовий статус\"]');bulk.value='';bulk.dispatchEvent(new Event('change',{bubbles:true}));");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('[data-testid=availability-module] button')].find(e=>e.textContent==='Призначити обраним').click()");
            await Until(()=>Task.FromResult(bridge.Availability.Rows.Count(r=>r.Changed)==205));
            Assert.Equal("Новинка", bridge.Availability.Rows.Single(r=>r.Sku=="other").Next);
            Assert.All(bridge.Availability.Rows.Where(r=>r.Sku!="other"),r=>Assert.Equal("",r.Next));
            await Tab("Як користуватися"); await Capture("web-availability-help");
            var imagePath=Path.Combine(dir,"photo.png");
            using(var picture=new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(600,300,SixLabors.ImageSharp.Color.Red))
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(picture,imagePath);
            reply=await Request("images","loadFiles",new{paths=new[]{imagePath},options=new SkuMaster.Infrastructure.Images.ImageOptions{Size=1000,Format="png"}});
            Assert.False(reply.TryGetProperty("error",out _),reply.ToString());
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(e=>e.textContent==='Зображення').click()");
            await Tab("Зображення");
            await Until(async()=>await core.ExecuteScriptAsync("Boolean(document.querySelector('[data-testid=images-module] img'))")=="true");
            await core.ExecuteScriptAsync("document.querySelector('[data-testid=images-module] img').closest('button').click()");
            await Until(async()=>await core.ExecuteScriptAsync("Boolean(document.querySelector('[alt=Оригінал]'))")=="true");
            await core.ExecuteScriptAsync("[...document.querySelectorAll('[data-testid=images-module] button')].find(e=>e.textContent==='Застосувати та переглянути').click()");
            await Until(async()=>await core.ExecuteScriptAsync("Boolean(document.querySelector('[alt=\"Результат обробки\"]'))")=="true");
            await Capture("web-images-preview");
            var photoId=DesktopBridgeTests.Data(bridge.Images.Snapshot()).GetProperty("items")[0].GetProperty("id").GetString()!;
            reply=await Request("images","export",new{folder=dir,ids=new[]{photoId}});
            Assert.False(reply.TryGetProperty("error",out _),reply.ToString());
            Assert.True(File.Exists(Path.Combine(dir,"photo_1.png")));
            using(var picture=SixLabors.ImageSharp.Image.Load(Path.Combine(dir,"photo_1.png")))Assert.Equal(1000,picture.Width);
            await Tab("Завантаження");
            window.Width = 840; window.Height = 640;
            await Capture("web-compact-files");
            Assert.Equal("true", await core.ExecuteScriptAsync("document.documentElement.scrollWidth <= window.innerWidth"));
            Assert.Equal("[]", await core.ExecuteScriptAsync("window.__uiErrors"));
            // Produce real snapshot data for optional browser-only visual QA; never bundled into the installer.
            File.WriteAllText(Path.Combine(artifacts, "bridge-snapshot.json"), JsonSerializer.Serialize(bridge.Snapshot(), DesktopBridge.Json));
            await core.ExecuteScriptAsync("window.chrome.webview.postMessage({id:9999,module:'app',action:'closeConfirmed',data:{}})");
            await Task.Delay(100);
        }
        finally
        {
            bridge.Status.Invalidate(); bridge.Missing.Invalidate(); bridge.Availability.Invalidate(); window.Close();
            // WebView child processes can hold their isolated profile briefly after disposal.
        }
    });
    private static string FindArtifacts()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "Directory.Build.props"))) path = path.Parent;
        return Directory.CreateDirectory(Path.Combine(path?.FullName ?? Path.GetTempPath(), "artifacts", "ui-review")).FullName;
    }
    private static async Task Until(Func<Task<bool>> predicate)
    {
        var end = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < end) { if (await predicate()) return; await Task.Delay(100); }
        throw new TimeoutException("WebView did not reach the expected state.");
    }
    private static void OnSta(Func<Task> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try { await action(); } catch (Exception ex) { error = ex; }
                finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "WebView integration test timed out.");
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
