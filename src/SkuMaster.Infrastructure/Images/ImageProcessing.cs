using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace SkuMaster.Infrastructure.Images;

public sealed record ImageOptions
{
    public int Size { get; init; } = 1000;
    public string Mode { get; init; } = "crop";
    public string Format { get; init; } = "jpg";
    public int Quality { get; init; } = 92;
    public string Background { get; init; } = "#ffffff";
    public double X { get; init; } = .5;
    public double Y { get; init; } = .5;
    public double Zoom { get; init; } = 1;
    public int Rotation { get; init; }
    public bool Enhance { get; init; }
    public void Validate()
    {
        if(Size is <128 or >4096 || Mode is not ("crop" or "fit") || Format is not ("jpg" or "png" or "webp") || Quality is <1 or >100 || !double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Zoom) || X is <0 or >1 || Y is <0 or >1 || Zoom is <1 or >4 || Rotation is not (0 or 90 or 180 or 270))
            throw new ArgumentException("Перевірте розмір (128–4096), масштаб і формат зображення.");
        if(!System.Text.RegularExpressions.Regex.IsMatch(Background??"", "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Оберіть колір фону.");
    }
}

public static class ImageProcessing
{
    public static Image<Rgba32> Load(byte[] bytes)
    {
        var info=Image.Identify(bytes) ?? throw new InvalidDataException("Невідомий формат зображення.");
        if((long)info.Width*info.Height>40_000_000) throw new InvalidDataException("Зображення перевищує 40 мегапікселів.");
        var image=Image.Load<Rgba32>(bytes);
        image.Mutate(x=>x.AutoOrient());
        while(image.Frames.Count>1) image.Frames.RemoveFrame(image.Frames.Count-1);
        image.Metadata.ExifProfile=null;
        return image;
    }
    public static Image<Rgba32> Render(byte[] bytes,ImageOptions options)
    {
        options.Validate();
        using var image=Load(bytes);
        if(options.Rotation!=0) image.Mutate(x=>x.Rotate(options.Rotation));
        if(options.Mode=="crop")
        {
            int side=Math.Max(1,(int)(Math.Min(image.Width,image.Height)/options.Zoom));
            int left=(int)Math.Round((image.Width-side)*options.X), top=(int)Math.Round((image.Height-side)*options.Y);
            image.Mutate(x=>x.Crop(new Rectangle(left,top,side,side)).Resize(options.Size,options.Size));
        }
        else image.Mutate(x=>x.Resize(new ResizeOptions{Size=new Size(options.Size,options.Size),Mode=ResizeMode.Max}));
        var output=new Image<Rgba32>(options.Size,options.Size,Color.ParseHex(options.Background));
        output.Mutate(x=>x.DrawImage(image,new Point((options.Size-image.Width)/2,(options.Size-image.Height)/2),1));
        return output;
    }
    public static byte[] Encode(Image image,ImageOptions options)
    {
        using var output=new MemoryStream();
        if(options.Format=="jpg") image.Save(output,new JpegEncoder{Quality=options.Quality});
        else if(options.Format=="webp") image.Save(output,new WebpEncoder{Quality=options.Quality});
        else image.Save(output,new PngEncoder());
        return output.ToArray();
    }
    public static string Preview(Image image,int size=768)
    {
        using var small=image.Clone(x=>x.Resize(new ResizeOptions{Size=new Size(size,size),Mode=ResizeMode.Max}));
        return "data:image/jpeg;base64,"+Convert.ToBase64String(Encode(small,new(){Format="jpg",Quality=85}));
    }
}
