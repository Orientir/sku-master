using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SkuMaster.Infrastructure.Images;

public static class ImageDownloads
{
    private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromMinutes(3)};
    public static async Task<byte[]> DownloadAsync(string address,int limit,CancellationToken token=default)
    {
        var uri=new Uri(address);
        for(int redirect=0;redirect<6;redirect++)
        {
            if(uri.Scheme!="https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback) throw new InvalidDataException("Вкажіть публічне HTTPS-посилання.");
            foreach(var resolved in await Dns.GetHostAddressesAsync(uri.Host,token))
            {
                var ip=resolved.IsIPv4MappedToIPv6?resolved.MapToIPv4():resolved;
                var bytes=ip.GetAddressBytes();
                if(IPAddress.IsLoopback(ip)||ip.IsIPv6LinkLocal||ip.IsIPv6SiteLocal||ip.Equals(IPAddress.Any)||ip.Equals(IPAddress.IPv6Any)||
                   (bytes.Length==4&&(bytes[0] is 0 or 10 or 127 or >=224 || bytes[0]==169&&bytes[1]==254 || bytes[0]==172&&bytes[1] is >=16 and <=31 || bytes[0]==192&&bytes[1]==168)) ||
                   (bytes.Length==16&&(bytes[0]&254)==252))throw new InvalidDataException("Посилання має вести на публічний сайт.");
            }
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.UserAgent.ParseAdd("SkuMaster/1.5");
            using var response=await Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode is >=300 and <400 && response.Headers.Location is {} location){uri=new Uri(uri,location);continue;}
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>limit) throw new InvalidDataException("Файл завеликий.");
            using var input=await response.Content.ReadAsStreamAsync(token);
            using var output=new MemoryStream(); var buffer=new byte[65536]; int count;
            while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>limit)throw new InvalidDataException("Файл завеликий.");await output.WriteAsync(buffer.AsMemory(0,count),token);}
            return output.ToArray();
        }
        throw new InvalidDataException("Забагато перенаправлень посилання.");
    }
    public static string[] FindImages(string html,Uri page)
    {
        string[] Extract(string pattern)=>Regex.Matches(html,pattern,RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2)).Select(m=>WebUtility.HtmlDecode(m.Groups[1].Value)).Select(value=>Uri.TryCreate(page,value,out var uri)&&uri.Scheme=="https"?uri.AbsoluteUri:"").Where(x=>x.Length>0).Distinct().Take(60).ToArray();
        var gallery=Extract("data-zoom-img\\s*=\\s*[\"']([^\"']+)[\"']");
        if(gallery.Length>0)return gallery;
        return Extract("(?:src|href|content)\\s*=\\s*[\"']([^\"']+\\.(?:jpg|jpeg|png|webp)(?:\\?[^\"']*)?)[\"']");
    }
    public static async Task<string[]> DiscoverAsync(string address,CancellationToken token)
    {
        var uri=new Uri(address);
        if(Regex.IsMatch(uri.AbsolutePath,"\\.(jpg|jpeg|png|webp)$",RegexOptions.IgnoreCase)) return [uri.AbsoluteUri];
        var html=Encoding.UTF8.GetString(await DownloadAsync(address,8*1024*1024,token));
        var urls=FindImages(html,uri);
        if(urls.Length==0)throw new InvalidDataException("Фото не знайдено. Спробуйте пряме посилання на JPG, PNG або WebP чи додайте файл з комп’ютера.");
        return urls;
    }
}
