using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;
using SkiaSharp;

// Optional authored maps replace individual procedural textures, not the shader.
// Raw PNGs and the provenance report never enter the runtime payload.
internal static class WaterTextureArt
{
    internal static void Build(string root)
    {
        string output=Path.Combine(root,"out/disc");Directory.CreateDirectory(output);
        string sources=Path.Combine(root,WaterTextureCatalog.SourceDirectory);
        if(Directory.Exists(sources))foreach(string path in Directory.GetFiles(sources))
            if(Path.GetExtension(path).Equals(".png",StringComparison.OrdinalIgnoreCase)&&
               !Enumerable.Range(0,WaterTextureCatalog.Count).Any(i=>Path.GetFileName(path)==WaterTextureCatalog.Role(i)+".png"))
                throw new InvalidDataException("Unknown water PNG filename; see docs/water-art-assets.md: "+Path.GetFileName(path));
        var packages=new byte[]?[WaterTextureCatalog.Count];
        var provenance=new List<object>();
        // Validate every supplied source before mutating the previous cooked set.
        for(int i=0;i<WaterTextureCatalog.Count;i++){
            string source=WaterTextureCatalog.Source(i),path=Path.Combine(root,source);
            if(File.Exists(path)){
                using var image=SKBitmap.Decode(path)??throw new InvalidDataException("Cannot decode optional water PNG: "+source);
                packages[i]=Encode(image,i);
            }
            provenance.Add(new {
                role=WaterTextureCatalog.Role(i),source,authored=packages[i]!=null,
                sourceSha256=packages[i]==null?null:Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                runtime=packages[i]==null?null:WaterTextureCatalog.DiscName(i),
                nativeFormat=WaterTextureCatalog.NativeFormat(i),width=WaterTextureCatalog.Edge(i),height=WaterTextureCatalog.Edge(i),
                mipLevels=1,nativeBytes=WaterTextureCatalog.NativeBytes(i),
                packageSha256=packages[i]==null?null:Convert.ToHexString(SHA256.HashData(packages[i]!)),
                fallback="procedural"
            });
        }
        for(int i=0;i<WaterTextureCatalog.Count;i++){
            string path=Path.Combine(output,WaterTextureCatalog.DiscName(i));
            if(packages[i]==null)File.Delete(path);else File.WriteAllBytes(path,packages[i]!);
        }
        File.WriteAllText(Path.Combine(root,"out/water-textures.json"),JsonSerializer.Serialize(new {
            schema=1,totalNativeBytes=WaterTextureCatalog.TotalNativeBytes,textures=provenance
        },new JsonSerializerOptions{WriteIndented=true})+"\n");
        Console.WriteLine("Water artwork: "+packages.Count(p=>p!=null)+" optional native maps; missing maps use procedural fallbacks");
    }
    internal static byte[] Encode(SKBitmap image,int index)
    {
        int edge=WaterTextureCatalog.Edge(index),format=WaterTextureCatalog.NativeFormat(index);
        string role=WaterTextureCatalog.Role(index);bool slopes=role=="surface-slopes";
        if(image.Width!=edge||image.Height!=edge)throw new InvalidDataException(role+" must be exactly "+edge+"x"+edge);
        var rgba=new byte[edge*edge*4];
        for(int y=0;y<edge;y++)for(int x=0;x<edge;x++){
            var c=image.GetPixel(x,y);int at=(y*edge+x)*4;
            if((format!=3||slopes)&&c.Alpha!=255)throw new InvalidDataException(role+" must be opaque; coverage belongs in grayscale, not PNG alpha");
            if(slopes){
                if(c.Red!=128)throw new InvalidDataException(role+" requires neutral red=128, blue=tangent X and green=tangent Z");
                // Author in an opaque PNG so image editors cannot premultiply
                // vector data. Pack the two independent components losslessly.
                rgba[at]=rgba[at+1]=rgba[at+2]=c.Green;rgba[at+3]=c.Blue;
            }else{
                if((format==1||format==3)&&(c.Red!=c.Green||c.Red!=c.Blue))throw new InvalidDataException(role+" requires grayscale RGB channels");
                rgba[at]=c.Red;rgba[at+1]=c.Green;rgba[at+2]=c.Blue;rgba[at+3]=c.Alpha;
            }
        }
        byte[] native;
        if(format==1){
            native=new byte[edge*edge];int at=0;
            for(int by=0;by<edge;by+=4)for(int bx=0;bx<edge;bx+=8)
                for(int y=0;y<4;y++)for(int x=0;x<8;x++)native[at++]=rgba[((by+y)*edge+bx+x)*4];
        }else native=TexturePixels.Encode(rgba,edge,edge,format);
        var package=new byte[32+native.Length];
        void Put(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(at),value);
        Put(0,0x58544e44);Put(4,2);Put(8,format);Put(12,edge);Put(16,edge);Put(20,native.Length);Put(24,1);
        native.CopyTo(package,32);WaterTextureCatalog.Validate(package,index);return package;
    }
    internal static void ValidateFile(string path,int index)
    {
        if(new FileInfo(path).Length!=32+WaterTextureCatalog.NativeBytes(index))throw new InvalidDataException("Water texture exceeds its native budget: "+path);
        try{WaterTextureCatalog.Validate(File.ReadAllBytes(path),index);}
        catch(InvalidOperationException e){throw new InvalidDataException("Invalid optional water texture: "+path,e);}
    }
    internal static void AddPayload(string directory,ISet<string> files)
    {
        for(int i=0;i<WaterTextureCatalog.Count;i++){
            string name=WaterTextureCatalog.DiscName(i),path=Path.Combine(directory,name);
            if(!File.Exists(path))continue;
            ValidateFile(path,i);files.Add(name);
        }
    }
    internal static void CopyPayload(string source,string destination)
    {
        for(int i=0;i<WaterTextureCatalog.Count;i++){
            string name=WaterTextureCatalog.DiscName(i),path=Path.Combine(source,name);
            if(!File.Exists(path))continue;
            ValidateFile(path,i);File.Copy(path,Path.Combine(destination,name),true);
        }
    }
    internal static void VerifyMatchingPayload(string disc,string app)
    {
        for(int i=0;i<WaterTextureCatalog.Count;i++){
            string name=WaterTextureCatalog.DiscName(i),source=Path.Combine(disc,name),installed=Path.Combine(app,name);
            bool present=File.Exists(source);
            if(present!=File.Exists(installed))throw new InvalidDataException("Optional water artwork differs between ISO and Depot: "+name);
            if(present){ValidateFile(source,i);ValidateFile(installed,i);}
            if(present&&!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(installed)))throw new InvalidDataException("Depot water artwork differs from the console build: "+name);
        }
    }
}
