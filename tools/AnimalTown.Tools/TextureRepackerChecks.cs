using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DolphinDotNet.Imaging;

internal static class TextureRepackerChecks
{
    internal static void Run(string root)
    {
        string folder=Path.Combine(root,"out/repacker-checks");Directory.CreateDirectory(folder);int checks=0;
        void Check(bool condition,string label){checks++;if(!condition)throw new Exception("Repacker: "+label);}
        var json=JsonNode.Parse("""{"asset":{"version":"2.0"},"buffers":[{"byteLength":0}],"bufferViews":[],"accessors":[],"images":[],"samplers":[{"wrapS":33071,"wrapT":33071}],"textures":[{"source":0,"sampler":0}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}}],"meshes":[{"primitives":[{"attributes":{},"material":0}]}],"nodes":[{"mesh":0,"skin":0},{"name":"joint"}],"skins":[{"joints":[1]}],"animations":[],"scenes":[{"nodes":[0,1]}],"scene":0}""")!.AsObject();
        using var bin=new MemoryStream();
        int View(byte[] data){while(bin.Length%4!=0)bin.WriteByte(0);int id=json["bufferViews"]!.AsArray().Count;json["bufferViews"]!.AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=(int)bin.Length,["byteLength"]=data.Length});bin.Write(data);return id;}
        int Access(byte[] data,int type,string shape,int count){int id=json["accessors"]!.AsArray().Count;json["accessors"]!.AsArray().Add(new JsonObject{["bufferView"]=View(data),["componentType"]=type,["type"]=shape,["count"]=count});return id;}
        byte[] Floats(params float[] values){byte[] result=new byte[values.Length*4];for(int i=0;i<values.Length;i++)BitConverter.GetBytes(values[i]).CopyTo(result,i*4);return result;}
        using(var image=new RasterImage(64,64,new Rgba32(255,255,255,255))){
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)image[x,y]=y<32?(x<32?new(255,0,0,255):new(0,0,255,255)):x>=32?new(255,0,0,255):new((byte)(x*7),(byte)((y-32)*7),80,255);
            using var png=new MemoryStream();image.SaveAsPng(png);json["images"]!.AsArray().Add(new JsonObject{["bufferView"]=View(png.ToArray()),["mimeType"]="image/png"});
        }
        var primitive=json["meshes"]![0]!["primitives"]![0]!;var attrs=primitive["attributes"]!;
        var positions=Floats(0,0,0, 1,0,0, 0,1,0, 2,0,0, 3,0,0, 2,1,0, 4,0,0, 5,0,0, 4,1,0, 6,0,0, 7,0,0, 6,1,0);
        attrs["POSITION"]=Access(positions,5126,"VEC3",12);
        attrs["TEXCOORD_0"]=Access(Floats(.06f,.06f,.2f,.06f,.06f,.2f, .6f,.6f,.8f,.6f,.6f,.8f, .6f,.06f,.8f,.06f,.6f,.2f, .06f,.56f,.4f,.56f,.06f,.9f),5126,"VEC2",12);
        var joints=new byte[48];for(int i=0;i<12;i++)joints[i*4+1]=1;
        attrs["JOINTS_0"]=Access(joints,5121,"VEC4",12);
        var weights=Floats(Enumerable.Range(0,48).Select(i=>i%4==0?.75f:i%4==1?.25f:0).ToArray());attrs["WEIGHTS_0"]=Access(weights,5126,"VEC4",12);
        int time=Access(Floats(0,1),5126,"SCALAR",2),track=Access(Floats(0,0,0,1,2,3),5126,"VEC3",2);
        var bind=Floats(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);json["skins"]![0]!["inverseBindMatrices"]=Access(bind,5126,"MAT4",1);
        json["animations"]!.AsArray().Add(JsonNode.Parse($$$"""{"name":"Motion","samplers":[{"input":{{{time}}},"output":{{{track}}},"interpolation":"LINEAR"}],"channels":[{"sampler":0,"target":{"node":1,"path":"translation"}}]}"""));
        json["buffers"]![0]!["byteLength"]=(int)bin.Length;string input=Path.Combine(folder,"source.glb");File.WriteAllBytes(input,TextureRepacker.EncodeGlb(json,bin.ToArray()));byte[] source=File.ReadAllBytes(input);
        foreach(int? size in new int?[]{null,128,256,512,1024}){
            string output=Path.Combine(folder,"packed-"+(size?.ToString()??"original")+".glb");TextureRepacker.Repack(input,output,size,.01);
            byte[] b=File.ReadAllBytes(output);int n=(int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12));var j=JsonNode.Parse(b.AsSpan(20,n))!;int start=28+n;
            byte[] Read(int a){var accessor=j["accessors"]![a]!;var view=j["bufferViews"]![(int)accessor["bufferView"]!]!;int offset=start+((int?)view["byteOffset"]??0)+((int?)accessor["byteOffset"]??0);return b.AsSpan(offset,(int)view["byteLength"]!).ToArray();}
            var pa=j["meshes"]![0]!["primitives"]![0]!["attributes"]!;
            Check(Read((int)pa["POSITION"]!).SequenceEqual(positions),"positions/winding unchanged");Check(Read((int)pa["JOINTS_0"]!).SequenceEqual(joints),"joint bytes unchanged");Check(Read((int)pa["WEIGHTS_0"]!).SequenceEqual(weights),"skin weights unchanged");
            var sampler=j["animations"]![0]!["samplers"]![0]!;Check(Read((int)sampler["input"]!).SequenceEqual(Floats(0,1))&&Read((int)sampler["output"]!).SequenceEqual(Floats(0,0,0,1,2,3)),"animation streams retained/remapped");
            Check(j["nodes"]!.ToJsonString()==json["nodes"]!.ToJsonString(),"hierarchy and skin attachment unchanged");
            Check(Read((int)j["skins"]![0]!["inverseBindMatrices"]!).SequenceEqual(bind),"inverse bind matrix preserved");
            var iv=j["bufferViews"]![(int)j["images"]![0]!["bufferView"]!]!;using var image=RasterImage.Load(b.AsSpan(start+(int)iv["byteOffset"]!,(int)iv["byteLength"]!).ToArray());Check(image.Width==(size??64)&&image.Height==(size??64),"requested dimensions");
            var report=JsonNode.Parse(File.ReadAllText(output+".repack.json"))!;Check((int)report["stackedSolidTriangles"]! ==1,"two distinct red regions stack");Check((int)report["images"]![0]!["sharedSolidColors"]! ==2,"red and blue never merge");Check((double)report["images"]![0]!["rgbaRmse"]!<.01,"original appearance retained");
            Check(File.ReadAllBytes(input).SequenceEqual(source),"artist source untouched");
        }
        foreach(bool alpha in new[]{false,true}){
            var variant=(JsonObject)json.DeepClone();var view=json["bufferViews"]![(int)json["images"]![0]!["bufferView"]!]!;
            using var image=RasterImage.Load(bin.GetBuffer().AsSpan((int)view["byteOffset"]!,(int)view["byteLength"]!).ToArray());
            for(int y=32;y<64;y++)for(int x=32;x<64;x++)image[x,y]=alpha?new Rgba32(255,0,0,128):new Rgba32(254,0,0,255);
            using var png=new MemoryStream();image.SaveAsPng(png);int vi=View(png.ToArray());variant["bufferViews"]=json["bufferViews"]!.DeepClone();variant["images"]![0]!["bufferView"]=vi;variant["buffers"]![0]!["byteLength"]=(int)bin.Length;
            string near=Path.Combine(folder,alpha?"alpha.glb":"near.glb"),target=Path.Combine(folder,alpha?"alpha-packed.glb":"near-packed.glb");File.WriteAllBytes(near,TextureRepacker.EncodeGlb(variant,bin.ToArray()));TextureRepacker.Repack(near,target,null,.01);
            var report=JsonNode.Parse(File.ReadAllText(target+".repack.json"))!;Check((int)report["stackedSolidTriangles"]! ==0,alpha?"different alpha never merges":"near-identical RGB never merges");
        }
        bool refused=false;try{TextureRepacker.Repack(input,input,null,.01);}catch(ArgumentException){refused=true;}Check(refused,"refuse overwrite source");
        var bad=(JsonObject)json.DeepClone();bad["buffers"]![0]!["byteLength"]=(int)bin.Length;bad["materials"]![0]!["normalTexture"]=new JsonObject{["index"]=0};string invalid=Path.Combine(folder,"unsupported.glb");File.WriteAllBytes(invalid,TextureRepacker.EncodeGlb(bad,bin.ToArray()));string rejected=Path.Combine(folder,"rejected.glb");
        refused=false;try{TextureRepacker.Repack(invalid,rejected,256,8);}catch(InvalidDataException){refused=true;}Check(refused&&!File.Exists(rejected),"unsupported secondary maps fail without output");
        // Force an atlas too small for the gradient chart and verify the quality
        // gate refuses its lossy resampling rather than blessing a damaged model.
        refused=false;try{TextureRepacker.Repack(input,rejected,16,0);}catch(InvalidDataException){refused=true;}Check(refused&&!File.Exists(rejected),"appearance gate rejects lossy output");
        Console.WriteLine($"Texture repacker: {checks} checks passed (all five size policies, UV sharing, skinning, animation, source safety and quality rejection)");
    }
}
