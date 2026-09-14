using SkuMaster.Infrastructure.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SkuMaster.Tests;
public class ImageModuleTests
{
    [Fact]
    public void GalleryPrefersZoomImagesAndDeduplicates()
    {
        var html = "<img src='/logo.png'><a data-zoom-img='https://nl.makitamedia.com/tool.jpg'></a><img src='https://nl.makitamedia.com/tool.jpg'><a data-zoom-img='/second.png'></a>";
        Assert.Equal(new[]{"https://nl.makitamedia.com/tool.jpg","https://www.makita.nl/second.png"}, ImageDownloads.FindImages(html,new Uri("https://www.makita.nl/artikel/test.html")));
    }
    [Fact]
    public void CropAndPaddingPreserveSquareDimensionsAndPosition()
    {
        using var input=new Image<Rgba32>(400,200,Color.Red);
        for(var y=0;y<200;y++) for(var x=200;x<400;x++) input[x,y]=Color.Blue;
        using var stream=new MemoryStream(); input.SaveAsPng(stream);
        var bytes=stream.ToArray();
        using var crop=ImageProcessing.Render(bytes,new(){Size=128,X=1});
        Assert.Equal(128,crop.Width); Assert.Equal(128,crop.Height);
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(),crop[64,64]);
        using var fit=ImageProcessing.Render(bytes,new(){Size=128,Mode="fit"});
        Assert.Equal(Color.White.ToPixel<Rgba32>(),fit[64,0]);
        Assert.Throws<ArgumentException>(()=>ImageProcessing.Render(bytes,new(){Size=0}));
    }
}
