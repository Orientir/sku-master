using System.IO;
using System.Text.Json;
using SkuMaster.Desktop.Images;
using SkuMaster.Infrastructure.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static SkuMaster.Desktop.Tests.DesktopBridgeTests;

namespace SkuMaster.Desktop.Tests;
public class ImagesWorkflowTests
{
    [Fact]
    public async Task RemoveSelectedIsAtomicAndPreservesOriginalFiles()
    {
        var dir=Directory.CreateTempSubdirectory("sku-remove-images-").FullName;
        try
        {
            var path=Path.Combine(dir,"tool.png");
            using(var image=new Image<Rgba32>(128,128,Color.Red))image.SaveAsPng(path);
            var vm=new ImagesViewModel(Path.Combine(dir,"profile"));
            await vm.ExecuteAsync("loadFiles",Data(new{paths=new[]{path,path,path},options=new ImageOptions()}));
            var ids=Data(vm.Snapshot()).GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetString()!).ToArray();
            await Assert.ThrowsAsync<InvalidDataException>(()=>vm.ExecuteAsync("removeSelected",Data(new{ids=new[]{ids[0],"missing"}})));
            Assert.Equal(3,Data(vm.Snapshot()).GetProperty("items").GetArrayLength());
            await vm.ExecuteAsync("removeSelected",Data(new{ids=new[]{ids[0],ids[2],ids[0]}}));
            Assert.Equal(ids[1],Data(vm.Snapshot()).GetProperty("items")[0].GetProperty("id").GetString());
            Assert.True(vm.Unsaved);
            await vm.ExecuteAsync("removeSelected",Data(new{ids=new[]{ids[1]}}));
            Assert.Empty(Data(vm.Snapshot()).GetProperty("items").EnumerateArray());
            Assert.False(vm.Unsaved);
            Assert.True(File.Exists(path));
        }
        finally{Directory.Delete(dir,true);}
    }
    [Fact]
    public async Task DefaultsImportAndIndividualSettingsAreIsolatedAndExportNeverOverwrites()
    {
        var dir=Directory.CreateTempSubdirectory("sku-images-").FullName;
        try
        {
            var path=Path.Combine(dir,"tool.png");using(var image=new Image<Rgba32>(600,300,Color.Red))image.SaveAsPng(path);
            var vm=new ImagesViewModel(Path.Combine(dir,"profile"));
            await vm.ExecuteAsync("defaults",Data(new{options=new ImageOptions{Size=256,Mode="fit"}}));
            Assert.Equal(256,new ImagesViewModel(Path.Combine(dir,"profile")).Defaults.Size);
            await vm.ExecuteAsync("loadFiles",Data(new{paths=new[]{path},options=new ImageOptions{Size=512}}));
            var first=Data(vm.Snapshot()).GetProperty("items")[0];var id=first.GetProperty("id").GetString()!;
            Assert.Equal(512,first.GetProperty("options").GetProperty("size").GetInt32());
            var preview=await vm.ExecuteAsync("preview",Data(new{id,options=new ImageOptions{Size=128,Format="png",X=1}}));
            Assert.StartsWith("data:image/png;base64,",Data(preview!).GetProperty("preview").GetString());
            Assert.Equal(256,vm.Defaults.Size);
            var saved=await vm.ExecuteAsync("export",Data(new{folder=dir,ids=new[]{id}}));
            var output=Data(saved!).GetProperty("paths")[0].GetString()!;
            Assert.NotEqual(path,output);
            using(var image=Image.Load(path))Assert.Equal(600,image.Width);
            using(var image=Image.Load(output)){Assert.Equal(128,image.Width);Assert.Equal(128,image.Height);}
            Assert.False(vm.Unsaved);
            await vm.ExecuteAsync("defaults",Data(new{options=new ImageOptions{Size=1000}}));
            Assert.Equal(128,Data(vm.Snapshot()).GetProperty("items")[0].GetProperty("options").GetProperty("size").GetInt32());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>vm.ExecuteAsync("preview",Data(new{id,options=new ImageOptions{Enhance=true}})));
            Assert.False(vm.IsBusy);
        }
        finally{Directory.Delete(dir,true);}
    }
}
