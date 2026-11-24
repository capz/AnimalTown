using System.Buffers.Binary;
using System.Text.Json;
using AnimalTown;
using SkiaSharp;

internal static class WaterTextureArtChecks
{
    internal static void Run(string project)
    {
        string root=Path.Combine(project,"out/water-art-check-"+Guid.NewGuid().ToString("N"));
        string source=Path.Combine(root,WaterTextureCatalog.SourceDirectory),disc=Path.Combine(root,"out/disc");
        Directory.CreateDirectory(source);int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new InvalidDataException("Water art check failed: "+message);}
        void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
        try{
            WaterTextureArt.Build(root);
            Check(!Directory.GetFiles(disc,"*.dndtex").Any(),"an absent art pack requires no runtime files");
            Check(WaterTextureCatalog.IndexOfRole("../surface-ripples")==-1&&WaterTextureCatalog.IndexOfDiscName("water-unknown.dndtex")==-1,"only fixed role names resolve");
            int total=0;
            for(int i=0;i<WaterTextureCatalog.Count;i++){
                using var image=Fixture(i);
                byte[] package=WaterTextureArt.Encode(image,i);int edge=WaterTextureCatalog.Edge(i),format=WaterTextureCatalog.NativeFormat(i);
                bool slopes=WaterTextureCatalog.Role(i)=="surface-slopes";
                total+=package.Length-32;
                for(int y=0;y<edge;y++)for(int x=0;x<edge;x++){
                    var expected=image.GetPixel(x,y);uint actual=TexturePixels.Pixel(package,x,y);
                    int r=(int)(actual>>24),g=(int)((actual>>16)&255),b=(int)((actual>>8)&255),a=(int)(actual&255);
                    if(slopes)Check(r==expected.Green&&g==expected.Green&&b==expected.Green&&a==expected.Blue,
                        "slope X/Z survive lossless IA8 packing without alpha-coverage semantics");
                    else Check(Math.Abs(r-expected.Red)<=(format==4?7:0)&&Math.Abs(g-expected.Green)<=(format==4?3:0)&&
                          Math.Abs(b-expected.Blue)<=(format==4?7:0)&&a==expected.Alpha,"GX tile order and channel round-trip: "+WaterTextureCatalog.Role(i));
                }
                using var png=image.Encode(SKEncodedImageFormat.Png,100);
                File.WriteAllBytes(Path.Combine(root,WaterTextureCatalog.Source(i)),png.ToArray());
                var broken=(byte[])package.Clone();BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(24),2);
                Reject(()=>WaterTextureCatalog.Validate(broken,i),"extra mip level rejected");
                broken=(byte[])package.Clone();BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(8),6);
                Reject(()=>WaterTextureCatalog.Validate(broken,i),"role format mismatch rejected");
                Reject(()=>WaterTextureCatalog.Validate(package[..^1],i),"truncated native package rejected");
                using var wrongSize=new SKBitmap(edge/2,edge/2);wrongSize.Erase(SKColors.White);
                Reject(()=>WaterTextureArt.Encode(wrongSize,i),"incorrect authoring dimensions rejected");
                if(format!=3||slopes){image.SetPixel(0,0,new SKColor(128,128,128,128));Reject(()=>WaterTextureArt.Encode(image,i),"non-foam alpha rejected");}
                if(slopes){image.SetPixel(0,0,new SKColor(255,128,128,255));Reject(()=>WaterTextureArt.Encode(image,i),"ordinary RGB normal map is not silently accepted as slopes");}
                else if(format==1||format==3){image.SetPixel(0,0,new SKColor(255,0,0,255));Reject(()=>WaterTextureArt.Encode(image,i),"colored intensity mask rejected");}
            }
            Check(total==WaterTextureCatalog.TotalNativeBytes&&total==65536,"full authored set matches the 64 KiB native budget");
            var originals=Directory.GetFiles(source,"*.png").ToDictionary(p=>p,File.ReadAllBytes);
            WaterTextureArt.Build(root);
            var cooked=Directory.GetFiles(disc,"*.dndtex").ToDictionary(p=>p,File.ReadAllBytes);
            Check(cooked.Count==WaterTextureCatalog.Count,"all six exact runtime names cooked");
            WaterTextureArt.Build(root);
            foreach(var pair in originals)Check(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value),"artist source preserved");
            foreach(var pair in cooked)Check(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value),"repeat cooking is deterministic");
            using(var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"out/water-textures.json")))){
                Check(report.RootElement.GetProperty("textures").EnumerateArray().All(t=>t.GetProperty("authored").GetBoolean()),"provenance identifies every authored replacement");
            }
            var payload=new SortedSet<string>(StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(disc,"water-unrecognized.dndtex"),"not an allowlisted asset");
            WaterTextureArt.AddPayload(disc,payload);
            Check(payload.Count==WaterTextureCatalog.Count&&!payload.Contains("water-unrecognized.dndtex"),"optional payload uses a closed role allowlist");
            string app=Path.Combine(root,"app");Directory.CreateDirectory(app);
            WaterTextureArt.CopyPayload(disc,app);WaterTextureArt.VerifyMatchingPayload(disc,app);
            Check(Directory.GetFiles(app).Length==WaterTextureCatalog.Count,"only cooked role files copied to Depot staging");
            string ripple=Path.Combine(app,WaterTextureCatalog.DiscName(1));
            File.Delete(ripple);Reject(()=>WaterTextureArt.VerifyMatchingPayload(disc,app),"missing optional file cannot silently differ between ISO and Depot");
            WaterTextureArt.CopyPayload(disc,app);var changed=File.ReadAllBytes(ripple);changed[32]^=1;File.WriteAllBytes(ripple,changed);
            Reject(()=>WaterTextureArt.VerifyMatchingPayload(disc,app),"different optional art bytes rejected");
            string typedWrong=Path.Combine(source,"surface-ripple.png");File.Copy(Path.Combine(root,WaterTextureCatalog.Source(1)),typedWrong);
            Reject(()=>WaterTextureArt.Build(root),"misspelled artist filename rejected instead of silently ignored");File.Delete(typedWrong);
            foreach(var pair in cooked)Check(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value),"invalid source pack does not partially replace cooked artwork");
            File.Delete(Path.Combine(root,WaterTextureCatalog.Source(1)));WaterTextureArt.Build(root);
            Check(!File.Exists(Path.Combine(disc,WaterTextureCatalog.DiscName(1))),"removed authoring source retires its stale cooked replacement");
            var invalid=File.ReadAllBytes(Path.Combine(disc,WaterTextureCatalog.DiscName(0)));invalid[8]=6;
            File.WriteAllBytes(Path.Combine(disc,WaterTextureCatalog.DiscName(0)),invalid);
            Reject(()=>WaterTextureArt.AddPayload(disc,new HashSet<string>()),"invalid optional package never enters the Depot payload");
            Console.WriteLine(checks+" water art checks passed: native channels, budgets, optional fallback packaging, source preservation and deterministic cooking");
        }finally{Directory.Delete(root,true);}
    }
    internal static SKBitmap Fixture(int index)
    {
        int edge=WaterTextureCatalog.Edge(index),format=WaterTextureCatalog.NativeFormat(index);
        var image=new SKBitmap(edge,edge,SKColorType.Rgba8888,SKAlphaType.Unpremul);
        for(int y=0;y<edge;y++)for(int x=0;x<edge;x++){
            byte intensity=(byte)((x*13+y*7)&255);
            image.SetPixel(x,y,WaterTextureCatalog.Role(index)=="surface-slopes"?new SKColor(128,(byte)(y*11),(byte)(x*7),255):
                format==4?new SKColor((byte)(x*7),(byte)(y*11),(byte)(x*3+y*5),255):
                new SKColor(intensity,intensity,intensity,format==3?(byte)((y*9)&255):(byte)255));
        }
        return image;
    }
}
