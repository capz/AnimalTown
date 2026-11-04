using System.Text.Json;
using System.Text.Json.Nodes;
using AnimalTown;
using SkiaSharp;

internal static class TerrainCliffAssets
{
    internal static void Build(string root,int size=128)
    {
        if(size!=128&&size!=256)throw new InvalidDataException("Cliff texture size must be 128 or 256");
        string textures=Path.Combine(root,"assets/textures/environment/cliffs"),models=Path.Combine(root,"assets/models/terrain/cliffs");Directory.CreateDirectory(textures);Directory.CreateDirectory(models);
        using var source=SKBitmap.Decode(Path.Combine(textures,"source/cliff-soil-source.png"))??throw new InvalidDataException("Missing cliff soil source");
        foreach(int edge in new[]{128,256}){
            using var image=new SKBitmap(edge,edge,SKColorType.Rgba8888,SKAlphaType.Opaque);
            using(var canvas=new SKCanvas(image))using(var src=SKImage.FromBitmap(source))canvas.DrawImage(src,new SKRect(0,0,edge,edge),new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Linear));
            // Pair edge pixels and feather a four-pixel band. Repeating wall UVs
            // therefore cannot expose the source image's slight boundary drift.
            for(int y=0;y<edge;y++)for(int x=0;x<4;x++){
                var a=image.GetPixel(x,y);var b=image.GetPixel(edge-1-x,y);float blend=(4-x)/4f;
                int r=(a.Red+b.Red)/2,g=(a.Green+b.Green)/2,bl=(a.Blue+b.Blue)/2;
                image.SetPixel(x,y,new SKColor((byte)(a.Red+(r-a.Red)*blend),(byte)(a.Green+(g-a.Green)*blend),(byte)(a.Blue+(bl-a.Blue)*blend)));
                image.SetPixel(edge-1-x,y,new SKColor((byte)(b.Red+(r-b.Red)*blend),(byte)(b.Green+(g-b.Green)*blend),(byte)(b.Blue+(bl-b.Blue)*blend)));
            }
            using var png=SKImage.FromBitmap(image).Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(Path.Combine(textures,$"cliff-soil-{edge}.png"),png.ToArray());
        }
        File.Copy(Path.Combine(textures,$"cliff-soil-{size}.png"),Path.Combine(textures,"cliff-soil.png"),true);
        var audit=new List<object>();for(int i=0;i<8;i++){var g=TerrainCliffGeometry.For(i);string file="cliff_"+TerrainCliffGeometry.Name(i)+"_2m.glb";WriteGlb(Path.Combine(models,file),g,i);audit.Add(new{file,orientation=TerrainCliffGeometry.Name(i),vertices=g.VertexCount,triangles=g.Triangles,footprint=new[]{2,2},rise=2,groundMaterial="surrounding-ground",groundUv="unrolled town X/Z divided by 2; never rotate with geometry",soilUv="all wall charts stacked in the same 0..1 rectangle; no wasted gutters",bottomFaces=0});}
        File.WriteAllText(Path.Combine(models,"cliff-kit.json"),JsonSerializer.Serialize(new{textureSize=size,soilTexture="../../../textures/environment/cliffs/cliff-soil.png",parts=audit},new JsonSerializerOptions{WriteIndented=true}));
        CookTexture(root);
        Console.WriteLine($"Cliff kit: eight exports, shared {size}px soil, 18 triangles/cardinal and 14/diagonal including lip and low tile; no hidden bottoms");
    }
    internal static void CookTexture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root,"out/disc"));
        using var image=SKBitmap.Decode(Path.Combine(root,"assets/textures/environment/cliffs/cliff-soil.png"))??throw new InvalidDataException("Missing cliff soil texture");
        if(image.Width!=image.Height||(image.Width!=128&&image.Width!=256))throw new InvalidDataException("Cliff soil must be 128x128 or 256x256");
        byte[] source=RepeatFloorAssets.Encode(image,new SKRectI(0,0,image.Width,image.Height));byte[] native=FloorTexture.Prepare(source);File.WriteAllBytes(Path.Combine(root,"out/disc/cliff-soil.dndtex"),native);
        Console.WriteLine($"Cliff soil: {image.Width}px, {native.Length-32} native bytes including mip");
    }
    private static void WriteGlb(string path,TerrainCliffGeometry g,int orientation)
    {
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        for(int i=0;i<g.VertexCount*3;i++)writer.Write(g.Positions[i]);int normalAt=(int)bytes.Position;
        for(int i=0;i<g.VertexCount*3;i++)writer.Write(g.Normals[i]);int uvAt=(int)bytes.Position;
        for(int i=0;i<g.VertexCount*2;i++)writer.Write(g.Uvs[i]);int indexAt=(int)bytes.Position;
        var indices=new List<ushort>();int groundCount=0;for(int group=0;group<2;group++)for(int t=0;t<g.Triangles;t++)if((g.Parts[t]==TerrainCliffGeometry.Soil?1:0)==group){for(int j=0;j<3;j++)indices.Add((ushort)g.Indices[t*3+j]);if(group==0)groundCount+=3;}
        foreach(ushort index in indices)writer.Write(index);while(bytes.Length%4!=0)writer.Write((byte)0);
        var json=JsonNode.Parse("""{"asset":{"version":"2.0","generator":"AnimalTown shared cliff kit"},"buffers":[{}],"bufferViews":[],"accessors":[],"images":[{"uri":"../../../textures/environment/cliffs/cliff-soil.png"}],"samplers":[{"wrapS":10497,"wrapT":33071,"minFilter":9987,"magFilter":9729}],"textures":[{"source":0,"sampler":0}],"materials":[{"name":"Bind surrounding ground (tops, low half, lip)","pbrMetallicRoughness":{"baseColorFactor":[0.447,0.725,0.333,1],"metallicFactor":0,"roughnessFactor":1},"extras":{"animaltownBinding":"surrounding-ground","uvSpace":"unrolled-world-xz","unitsPerRepeat":2}},{"name":"Shared soil / stacked UVs","pbrMetallicRoughness":{"baseColorTexture":{"index":0},"metallicFactor":0,"roughnessFactor":1}}],"meshes":[{"primitives":[]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}""")!;
        json["buffers"]![0]!["byteLength"]=bytes.Length;
        int[] offsets={0,normalAt,uvAt,indexAt,indexAt+groundCount*2},lengths={normalAt,uvAt-normalAt,indexAt-uvAt,groundCount*2,(indices.Count-groundCount)*2};
        for(int i=0;i<5;i++){json["bufferViews"]!.AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=offsets[i],["byteLength"]=lengths[i],["target"]=i<3?34962:34963});var a=new JsonObject{["bufferView"]=i,["componentType"]=i<3?5126:5123,["count"]=i<3?g.VertexCount:i==3?groundCount:indices.Count-groundCount,["type"]=i<2?"VEC3":i==2?"VEC2":"SCALAR"};if(i==0){a["min"]=new JsonArray(0,0,0);a["max"]=new JsonArray(2,2,2);}json["accessors"]!.AsArray().Add(a);}
        for(int i=0;i<2;i++)json["meshes"]![0]!["primitives"]!.AsArray().Add(new JsonObject{["attributes"]=new JsonObject{["POSITION"]=0,["NORMAL"]=1,["TEXCOORD_0"]=2},["indices"]=3+i,["material"]=i});
        json["nodes"]![0]!["name"]="cliff_"+TerrainCliffGeometry.Name(orientation);File.WriteAllBytes(path,TextureRepacker.EncodeGlb(json.AsObject(),bytes.ToArray()));
    }
}
