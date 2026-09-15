using System.IO;
using System.Text.Json;
using SkuMaster.Infrastructure;
using SkuMaster.Infrastructure.Images;

namespace SkuMaster.Desktop.Images;

public sealed class ImageItem
{
    public string Id { get; }=Guid.NewGuid().ToString("N");
    public required string Name { get; init; }
    public required byte[] Bytes { get; init; }
    public string? SourcePath { get; init; }
    public required string Preview { get; init; }
    public required string Thumbnail { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public required ImageOptions Options { get; set; }
    public byte[]? Enhanced { get; set; }
    public bool Saved { get; set; }
}

public sealed class ImagesViewModel : ObservableObject
{
    private readonly string root;
    private readonly LocalUpscaler engine;
    private CancellationTokenSource? cancellation;
    private readonly List<ImageItem> items=[];
    public ImagesViewModel(string? root=null)
    {
        this.root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SkuMaster","images");
        engine=new(Path.Combine(this.root,"engine"));
        try{if(File.Exists(SettingsPath)){Defaults=JsonSerializer.Deserialize<ImageOptions>(File.ReadAllText(SettingsPath))!;Defaults.Validate();}}
        catch{Defaults=new();Message="Не вдалося прочитати налаштування зображень; використано стандартні.";}
    }
    private string SettingsPath=>Path.Combine(root,"settings.json");
    public ImageOptions Defaults { get; private set; }=new();
    public bool IsBusy { get; private set; }
    public bool Unsaved=>items.Any(x=>!x.Saved);
    public string Message { get; private set; }="Додайте посилання або файли зображень.";
    public List<string> Warnings { get; }=[];
    public object Snapshot()=>new{defaults=Defaults,busy=IsBusy,message=Message,engineReady=engine.Installed,warnings=Warnings,
        items=items.Select(x=>new{x.Id,x.Name,x.Width,x.Height,x.Thumbnail,x.Options,x.Saved})};
    private ImageItem Find(string id)=>items.SingleOrDefault(x=>x.Id==id)??throw new InvalidDataException("Зображення не знайдено.");
    public void Cancel()=>cancellation?.Cancel();
    public async Task<object?> ExecuteAsync(string action,JsonElement data)
    {
        if(action=="cancel"){Cancel();return null;}
        if(IsBusy)throw new InvalidOperationException("Дочекайтеся завершення обробки.");
        var json=Web.DesktopBridge.Json;
        ImageOptions Options()=>data.GetProperty("options").Deserialize<ImageOptions>(json)??throw new InvalidDataException("Некоректні налаштування.");
        string Text(string key)=>data.GetProperty(key).GetString()!;
        if(action=="defaults")
        {
            var next=Options();next.Validate();Directory.CreateDirectory(root);
            FileService.AtomicWrite(SettingsPath,s=>JsonSerializer.Serialize(s,next));Defaults=next;Notify("");return null;
        }
        if(action=="source"){var item=Find(Text("id"));return new{source=item.Preview,item.Width,item.Height};}
        if(action=="draft"){Find(Text("id")).Saved=false;Notify("");return null;}
        if(action=="remove"){items.Remove(Find(Text("id")));Notify("");return null;}
        if(action=="removeSelected")
        {
            var removed=data.GetProperty("ids").EnumerateArray().Select(x=>Find(x.GetString()!)).ToHashSet();
            items.RemoveAll(removed.Contains);
            Message=$"Видалено з галереї: {removed.Count}. Файли на диску збережені.";Notify("");return null;
        }
        return await Run(async token=>
        {
            if(action=="installEngine"){Message="Завантаження AI-компонента (~45 МБ)…";Notify("");await engine.InstallAsync(token);Message="AI-компонент готовий. Обробка виконується локально.";return null;}
            if(action=="discover"){Message="Пошук фотографій…";Notify("");return new{urls=await ImageDownloads.DiscoverAsync(Text("url"),token)};}
            if(action is "loadUrls" or "loadFiles")
            {
                var options=Options();options.Validate();Warnings.Clear();
                var inputs=data.GetProperty("paths").EnumerateArray().Select(x=>x.GetString()!).ToArray();
                if(inputs.Length+items.Count>60)throw new InvalidDataException("В одній сесії можна відкрити до 60 зображень.");
                foreach(var path in inputs)
                {
                    token.ThrowIfCancellationRequested(); Message=$"Завантаження {items.Count+1}…";Notify("");
                    try
                    {
                        byte[] bytes;
                        if(action=="loadUrls") bytes=await ImageDownloads.DownloadAsync(path,30*1024*1024,token);
                        else {if(new FileInfo(path).Length>30*1024*1024)throw new InvalidDataException("Файл більший за 30 МБ.");bytes=await File.ReadAllBytesAsync(path,token);}
                        if(items.Sum(x=>(long)x.Bytes.Length)+bytes.Length>256L*1024*1024)throw new InvalidDataException("Загальний обсяг відкритих оригіналів перевищує 256 МБ. Збережіть і приберіть частину фотографій.");
                        var name=Path.GetFileNameWithoutExtension(action=="loadUrls"?new Uri(path).AbsolutePath:path);
                        var item=await Task.Run(()=>{using var image=ImageProcessing.Load(bytes);return new ImageItem{Bytes=bytes,Name=Uri.UnescapeDataString(name),SourcePath=action=="loadFiles"?path:null,Width=image.Width,Height=image.Height,Preview=ImageProcessing.Preview(image),Thumbnail=ImageProcessing.Preview(image,160),Options=options with{}};},token);
                        items.Add(item);
                    }
                    catch(Exception ex) when(ex is not OperationCanceledException){Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");}
                }
                Message=$"Відкрито {items.Count} зображень. Попереджень: {Warnings.Count}.";return null;
            }
            if(action=="preview")
            {
                var item=Find(Text("id"));var options=Options();options.Validate();
                if(item.Options!=options){item.Options=options;item.Saved=false;}
                var bytes=await Render(item,token);
                return new{preview="data:image/"+(options.Format=="jpg"?"jpeg":options.Format)+";base64,"+Convert.ToBase64String(bytes),size=bytes.Length};
            }
            if(action=="export")
            {
                var folder=Text("folder");if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Оберіть папку для збереження.");
                var ids=data.GetProperty("ids").EnumerateArray().Select(x=>x.GetString()!).Distinct().ToArray();
                var saved=new List<string>();
                foreach(var id in ids)
                {
                    var item=Find(id);Message=$"Збереження {saved.Count+1} із {ids.Length}…";Notify("");
                    var bytes=await Render(item,token);
                    var name=string.Concat(item.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim().TrimEnd('.');if(name.Length==0)name="image";
                    if(name.Length>100)name=name[..100];
                    var path=Path.Combine(folder,name+"."+item.Options.Format);int suffix=1;
                    while(File.Exists(path)||items.Any(x=>string.Equals(x.SourcePath,path,StringComparison.OrdinalIgnoreCase)))path=Path.Combine(folder,$"{name}_{suffix++}.{item.Options.Format}");
                    // CreateNew protects existing files even if another app creates the path after our check.
                    using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write)){try{await stream.WriteAsync(bytes,token);}catch{stream.Dispose();File.Delete(path);throw;}}
                    item.Saved=true;saved.Add(path);
                }
                Message=$"Збережено {saved.Count} зображень.";return new{paths=saved};
            }
            throw new InvalidDataException("Невідома дія зображень.");
        });
    }
    private async Task<byte[]> Render(ImageItem item,CancellationToken token)
    {
        var source=item.Bytes;
        if(item.Options.Enhance)
        {
            Message="AI покращує зображення. Це може тривати кілька хвилин…";Notify("");
            item.Enhanced??=await engine.EnhanceAsync(source,token);source=item.Enhanced;
        }
        return await Task.Run(()=>{token.ThrowIfCancellationRequested();using var result=ImageProcessing.Render(source,item.Options);return ImageProcessing.Encode(result,item.Options);},token);
    }
    private async Task<object?> Run(Func<CancellationToken,Task<object?>> work)
    {
        IsBusy=true;cancellation=new();Notify("");
        try{return await work(cancellation.Token);}
        catch(OperationCanceledException){Message="Обробку скасовано.";throw new InvalidOperationException(Message);}
        catch(Exception ex){Message=ex.Message;throw;}
        finally{IsBusy=false;cancellation.Dispose();cancellation=null;Notify("");}
    }
}
