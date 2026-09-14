using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using SixLabors.ImageSharp.Processing;

namespace SkuMaster.Infrastructure.Images;

public sealed class LocalUpscaler(string root)
{
    public const string PackageUrl="https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip";
    public const string PackageHash="ABC02804E17982A3BE33675E4D471E91EA374E65B70167ABC09E31ACB412802D";
    private string Engine => Path.Combine(root,"realesrgan-ncnn-vulkan.exe");
    public bool Installed => File.Exists(Engine) && File.Exists(Path.Combine(root,"vcomp140.dll")) && File.Exists(Path.Combine(root,"models","realesrgan-x4plus.bin")) && File.Exists(Path.Combine(root,"models","realesrgan-x4plus.param"));
    public async Task InstallAsync(CancellationToken token)
    {
        var bytes=await ImageDownloads.DownloadAsync(PackageUrl,60*1024*1024,token);
        if(Convert.ToHexString(SHA256.HashData(bytes))!=PackageHash)throw new InvalidDataException("Контрольна сума AI-компонента не збігається. Встановлення скасовано.");
        Directory.CreateDirectory(root);
        using var stream=new MemoryStream(bytes);using var zip=new ZipArchive(stream);
        foreach(var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if(entry.FullName is not ("realesrgan-ncnn-vulkan.exe" or "vcomp140.dll" or "models/realesrgan-x4plus.bin" or "models/realesrgan-x4plus.param" or "README_windows.md"))continue;
            var target=Path.Combine(root,entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input=entry.Open();FileService.AtomicWrite(target,output=>input.CopyTo(output),token);
        }
    }
    public async Task<byte[]> EnhanceAsync(byte[] bytes,CancellationToken token)
    {
        if(!Installed)throw new InvalidOperationException("Спочатку завантажте AI-компонент у налаштуваннях зображень.");
        var work=Directory.CreateTempSubdirectory("sku-ai-").FullName;
        try
        {
            var input=Path.Combine(work,"input.png");var output=Path.Combine(work,"output.png");
            // Normalize to RGBA PNG: this Windows engine can crash after processing some direct JPEG inputs.
            using(var image=ImageProcessing.Load(bytes))
            {
                if(Math.Max(image.Width,image.Height)>1024)image.Mutate(x=>x.Resize(new ResizeOptions{Size=new SixLabors.ImageSharp.Size(1024,1024),Mode=ResizeMode.Max}));
                await File.WriteAllBytesAsync(input,ImageProcessing.Encode(image,new(){Format="png"}),token);
            }
            var start=new ProcessStartInfo(Engine){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(var arg in new[]{"-i",input,"-o",output,"-m",Path.Combine(root,"models"),"-n","realesrgan-x4plus","-s","4","-t","128"})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new InvalidOperationException("Не вдалося запустити AI-компонент.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var error=process.StandardError.ReadToEndAsync();var stdout=process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);throw;}
            await error;await stdout;
            if(process.ExitCode!=0||!File.Exists(output))throw new InvalidOperationException("AI-обробка не вдалася. Потрібна відеокарта з Vulkan та актуальний драйвер. Можна вимкнути AI й зберегти звичайне зображення.");
            return await File.ReadAllBytesAsync(output,token);
        }
        finally { Directory.Delete(work,true); }
    }
}
