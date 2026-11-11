using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using AnimalTown;
using SkiaSharp;

internal static class BuildingAssets
{
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};
    // Sources keep standard glTF CCW faces and outward normals. The game's GX
    // camera/submission convention requires clockwise faces; convert only the
    // newly imported package, preserving author pivots, normals and source art.
    internal static void Native(string path)=>NativeModelWinding.ConvertFile(path);
    internal static void Generate(string root)
    {
        string exterior=Path.Combine(root,"assets/models/buildings/cottage_small"),interior=Path.Combine(root,"assets/models/interiors/cottage_small"),textures=Path.Combine(root,"assets/textures/buildings/cottage_small");
        Directory.CreateDirectory(exterior);Directory.CreateDirectory(interior);Directory.CreateDirectory(textures);
        using var atlas=new SKBitmap(128,128);SKColor[] palette={new(223,194,142),new(66,125,118),new(133,80,48),new(102,110,111),new(251,213,119),new(183,139,91),new(137,164,157),new(244,224,183),new(89,66,52),new(211,138,102),new(100,146,157),new(194,170,133),new(239,232,211),new(111,139,89),new(80,104,114),new(174,103,78)};
        for(int y=0;y<128;y++)for(int x=0;x<128;x++){var c=palette[y/32*4+x/32];int noise=((x*17+y*31)%7)-3;atlas.SetPixel(x,y,new SKColor((byte)Math.Clamp(c.Red+noise,0,255),(byte)Math.Clamp(c.Green+noise,0,255),(byte)Math.Clamp(c.Blue+noise,0,255)));}
        byte[] png;using(var encoded=SKImage.FromBitmap(atlas).Encode(SKEncodedImageFormat.Png,100))png=encoded.ToArray();File.WriteAllBytes(Path.Combine(textures,"cottage-atlas.png"),png);
        var body=new MeshData();body.Box(0,0,0,3,2.5f,.15f,0);body.Box(0,0,0,.15f,2.5f,3,0);body.Box(2.85f,0,0,.15f,2.5f,3,0);body.Box(0,0,2.85f,.95f,2.5f,.15f,0);body.Box(2.05f,0,2.85f,.95f,2.5f,.15f,0);body.Box(.95f,2.15f,2.85f,1.1f,.35f,.15f,0);
        body.Box(1,.012f,3.12f,1,.025f,.72f,9);
        body.Box(-.05f,-.1f,-.05f,3.1f,.15f,3.1f,3);body.Box(.87f,0,3.01f,.08f,2.24f,.1f,2);body.Box(2.05f,0,3.01f,.08f,2.24f,.1f,2);body.Box(.87f,2.15f,3.01f,1.26f,.09f,.1f,2);
        body.Box(.2f,.9f,3.01f,.55f,.8f,.06f,8);body.Box(.26f,.96f,3.075f,.43f,.68f,.01f,4);body.Box(2.25f,.9f,3.01f,.55f,.8f,.06f,8);body.Box(2.31f,.96f,3.075f,.43f,.68f,.01f,4);
        body.Quad(new(-.18f,2.5f,-.18f),new(-.18f,2.5f,3.18f),new(1.5f,3.55f,3.18f),new(1.5f,3.55f,-.18f),1);
        body.Quad(new(1.5f,3.55f,-.18f),new(1.5f,3.55f,3.18f),new(3.18f,2.5f,3.18f),new(3.18f,2.5f,-.18f),1);
        body.Triangle(new(0,2.5f,0),new(1.5f,3.55f,0),new(3,2.5f,0),0);body.Triangle(new(3,2.5f,3),new(1.5f,3.55f,3),new(0,2.5f,3),0);Write(Path.Combine(exterior,"exterior.glb"),body,png);
        var door=new MeshData();door.Box(0,0,0,1.1f,2.15f,.09f,2);door.Box(.12f,1.05f,.095f,.86f,.85f,.015f,4);door.Box(.91f,.88f,.1f,.08f,.08f,.06f,4);Write(Path.Combine(exterior,"door.glb"),door,png);Write(Path.Combine(interior,"door.glb"),door,png);
        var floor=new MeshData();floor.Box(0,-.08f,0,4,.08f,4,5);floor.Box(1.35f,.005f,2.85f,1.3f,.015f,1.1f,9);Write(Path.Combine(interior,"floor.glb"),floor,png);
        for(int wall=0;wall<4;wall++){
            var mesh=new MeshData();if(wall==0)mesh.Box(0,0,-.12f,4,2.8f,.12f,7);if(wall==1)mesh.Box(4,0,0,.12f,2.8f,4,7);if(wall==3)mesh.Box(-.12f,0,0,.12f,2.8f,4,7);
            if(wall==2){mesh.Box(0,0,4,1.45f,2.8f,.12f,7);mesh.Box(2.55f,0,4,1.45f,2.8f,.12f,7);mesh.Box(1.45f,2.15f,4,1.1f,.65f,.12f,7);}
            if(wall==0){mesh.Box(0,0,.005f,4,.12f,.04f,2);mesh.Box(1.35f,1.1f,.01f,1.3f,.95f,.04f,2);mesh.Box(1.43f,1.18f,.055f,1.14f,.79f,.015f,6);}
            Write(Path.Combine(interior,new[]{"north","east","south","west"}[wall]+".glb"),mesh,png);
        }
        string definitions=Path.Combine(root,"assets/definitions/buildings");Directory.CreateDirectory(definitions);
        File.WriteAllText(Path.Combine(definitions,"cottage_small.json"),JsonSerializer.Serialize(new{version=1,type=0,id="cottage_small",prefix="cottage",interior="cottage_small",footprint=new[]{3,3},hinge=new[]{.95,0,3.02},approach=new[]{1.5,0,3.5},exterior="assets/models/buildings/cottage_small/exterior.glb",door="assets/models/buildings/cottage_small/door.glb"},JsonOptions));
        definitions=Path.Combine(root,"assets/definitions/interiors");Directory.CreateDirectory(definitions);
        File.WriteAllText(Path.Combine(definitions,"cottage_small.json"),JsonSerializer.Serialize(new{version=1,id="cottage_small",size=4,units="metres",up="+Y",walls=new[]{"north","east","south","west"},door=new{center=new[]{2,0,4},arrival=new[]{2,0,3.35}},camera=new{pitch="fixed",quarterTurns=true,heightScale=.7,heightOffset=2.8,backScale=.9,backOffset=1.2},activities=new[]{new[]{3.0,2.0},new[]{1.8,2.2}},fixtures=new[]{new{x=.25,z=.35,width=1.15,depth=1.6,height=.5,kind=1},new{x=2.65,z=.35,width=1.0,depth=.7,height=.8,kind=2}},floor="assets/models/interiors/cottage_small/floor.glb",north="assets/models/interiors/cottage_small/north.glb",east="assets/models/interiors/cottage_small/east.glb",south="assets/models/interiors/cottage_small/south.glb",west="assets/models/interiors/cottage_small/west.glb"},JsonOptions));
        Cook(root);
    }
    internal static void Cook(string root)
    {
        string folder=Path.Combine(root,"assets/definitions/buildings");var files=Directory.GetFiles(folder,"*.json").OrderBy(p=>{using var d=JsonDocument.Parse(File.ReadAllBytes(p));return d.RootElement.GetProperty("type").GetInt32();}).ToArray();
        if(files.Length<1||files.Length>BuildingCatalog.MaximumDefinitions)throw new InvalidDataException("Building definition budget");
        var imports=new Dictionary<string,string>(StringComparer.Ordinal);var ids=new HashSet<string>();using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(0x424c4447);writer.Write(2);writer.Write(files.Length);
        foreach(string file in files){using var building=JsonDocument.Parse(File.ReadAllBytes(file));var b=building.RootElement;if(b.GetProperty("type").GetInt32()!=ids.Count)throw new InvalidDataException("Building type IDs must be contiguous and stable");string id=b.GetProperty("id").GetString()!,prefix=b.GetProperty("prefix").GetString()!;ValidateName(id);ValidateName(prefix);if(!ids.Add(id)||b.GetProperty("version").GetInt32()!=1)throw new InvalidDataException("Invalid building ID/version");
            string interiorId=b.GetProperty("interior").GetString()!;ValidateName(interiorId);using var interior=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"assets/definitions/interiors",interiorId+".json")));var r=interior.RootElement;int size=r.GetProperty("size").GetInt32();if(size<4||size>16||r.GetProperty("version").GetInt32()!=1)throw new InvalidDataException("Interior grid budget/version");
            if(b.GetProperty("footprint")[0].GetInt32()!=3||b.GetProperty("footprint")[1].GetInt32()!=3)throw new InvalidDataException("Exterior must fit the reserved 3m house footprint");
            Text(id);writer.Write(size);
            foreach(var socket in new[]{b.GetProperty("hinge")[0],b.GetProperty("hinge")[2],b.GetProperty("approach")[0],b.GetProperty("approach")[2]})writer.Write((int)(socket.GetSingle()*1024));
            float inset=size-r.GetProperty("door").GetProperty("arrival")[2].GetSingle();writer.Write((int)(inset*1024));
            var camera=r.GetProperty("camera");foreach(string field in new[]{"heightScale","heightOffset","backScale","backOffset"})writer.Write((int)(camera.GetProperty(field).GetSingle()*1024));
            var activities=r.GetProperty("activities");writer.Write(activities.GetArrayLength());foreach(var activity in activities.EnumerateArray()){writer.Write((int)(activity[0].GetSingle()*1024));writer.Write((int)(activity[1].GetSingle()*1024));}
            var fixtures=r.GetProperty("fixtures");if(fixtures.GetArrayLength()>16)throw new InvalidDataException("Interior fixture budget");writer.Write(fixtures.GetArrayLength());
            foreach(var fixture in fixtures.EnumerateArray()){
                float x=fixture.GetProperty("x").GetSingle(),z=fixture.GetProperty("z").GetSingle(),w=fixture.GetProperty("width").GetSingle(),d=fixture.GetProperty("depth").GetSingle(),h=fixture.GetProperty("height").GetSingle();int kind=fixture.GetProperty("kind").GetInt32();
                if(!float.IsFinite(x+z+w+d+h)||x<0||z<0||w<=0||d<=0||h<=0||h>3||x+w>size||z+d>size||kind<0||kind>2||BuildingWorld.DoorClearance(size,x,z,w,d))throw new InvalidDataException("Invalid fixture or blocked exit");
                foreach(float f in new[]{x,z,w,d,h,(float)kind})writer.Write((int)(f*1024));
            }
            foreach(string part in new[]{"exterior","door","floor","north","east","south","west"}){
                string source=(part=="exterior"||part=="door"?b:r).GetProperty(part).GetString()!;string full=Path.GetFullPath(Path.Combine(root,source));if(!full.StartsWith(Path.GetFullPath(Path.Combine(root,"assets/models"))+Path.DirectorySeparatorChar,StringComparison.Ordinal)||!full.EndsWith(".glb",StringComparison.Ordinal))throw new InvalidDataException("Building source escapes assets/models");
                Audit(full,part);string alias=prefix+"-"+part;if(!imports.TryAdd(alias,source))throw new InvalidDataException("Duplicate building runtime model");Text(alias);
            }
            // Derived output only: preserve supplied source art and the seven-name
            // catalog/save schema. The closed form is an optional eighth runtime part.
            string closed="out/derived-buildings/"+prefix+"-exterior-closed.glb";
            MergeClosed(Path.Combine(root,b.GetProperty("exterior").GetString()!),Path.Combine(root,b.GetProperty("door").GetString()!),Path.Combine(root,closed),b.GetProperty("hinge")[0].GetSingle(),b.GetProperty("hinge")[2].GetSingle());
            imports.Add(prefix+"-exterior-closed",closed);
        }
        Directory.CreateDirectory(Path.Combine(root,"out/disc"));byte[] bytes=stream.ToArray();BuildingCatalog.Configure(bytes);File.WriteAllBytes(Path.Combine(root,"out/disc/buildings.bin"),bytes);File.WriteAllText(Path.Combine(root,"out/building-assets.json"),JsonSerializer.Serialize(imports,JsonOptions));Console.WriteLine($"Building catalog: {files.Length} definitions, {imports.Count} shared model parts, {bytes.Length} descriptor bytes");
        void Text(string value){ValidateName(value);byte[] data=Encoding.ASCII.GetBytes(value);writer.Write(data.Length);writer.Write(data);}
    }
    internal static void Check(string root)
    {
        Cook(root);string temp=Path.Combine(Path.GetTempPath(),"animaltown-building-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try{
            foreach(string folder in new[]{"assets/definitions/buildings","assets/definitions/interiors","assets/models/buildings","assets/models/interiors"})
                foreach(string source in Directory.GetFiles(Path.Combine(root,folder),"*",SearchOption.AllDirectories)){string target=Path.Combine(temp,Path.GetRelativePath(root,source));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target);}
            string file=Path.Combine(temp,"assets/definitions/buildings/cottage_small.json"),inside=Path.Combine(temp,"assets/definitions/interiors/cottage_small.json");string original=File.ReadAllText(file),interior=File.ReadAllText(inside);
            void Reject(string label){bool failed=false;try{Cook(temp);}catch(Exception e)when(e is IOException||e is InvalidDataException||e is InvalidOperationException||e is KeyNotFoundException){failed=true;}if(!failed)throw new InvalidDataException(label+" accepted");File.WriteAllText(file,original);File.WriteAllText(inside,interior);}
            void Building(string key,object value){var node=System.Text.Json.Nodes.JsonNode.Parse(original)!;node[key]=JsonSerializer.SerializeToNode(value);File.WriteAllText(file,node.ToJsonString());}
            void Interior(string key,object value){var node=System.Text.Json.Nodes.JsonNode.Parse(interior)!;node[key]=JsonSerializer.SerializeToNode(value);File.WriteAllText(inside,node.ToJsonString());}
            Building("exterior","../outside.glb");Reject("source escape");Building("footprint",new[]{4,4});Reject("footprint mismatch");Building("hinge",new[]{20,0,3});Reject("door socket bounds");Building("type",2);Reject("type ID reorder");Interior("size",32);Reject("navigation grid overflow");Interior("activities",new[]{new[]{100,100}});Reject("activity bounds");Interior("fixtures",new[]{new{x=1.5,z=3,width=1,depth=.5,height=.5,kind=1}});Reject("blocked exit");
            string model=Path.Combine(temp,"assets/models/buildings/cottage_small/exterior.glb");byte[] modelBytes=File.ReadAllBytes(model);var oversized=new MeshData();for(int i=0;i<70;i++)oversized.Box(0,0,0,1,1,1,0);Write(model,oversized,File.ReadAllBytes(Path.Combine(root,"assets/textures/buildings/cottage_small/cottage-atlas.png")));Reject("triangle budget");File.WriteAllBytes(model,modelBytes);
            Cook(temp);
            var before=ReadMergePart(model,out var sourcePng,out _);var doorData=ReadMergePart(Path.Combine(temp,"assets/models/buildings/cottage_small/door.glb"),out _,out _);
            var combined=ReadMergePart(Path.Combine(temp,"out/derived-buildings/cottage-exterior-closed.glb"),out var mergedPng,out _);
            if(!sourcePng.AsSpan().SequenceEqual(mergedPng)||!combined.Normals.SequenceEqual(before.Normals.Concat(doorData.Normals))||!combined.Uvs.SequenceEqual(before.Uvs.Concat(doorData.Uvs))||!combined.Positions.Take(before.Positions.Count).SequenceEqual(before.Positions))throw new InvalidDataException("Merged building changed original attributes/atlas");
            for(int i=0;i<doorData.Positions.Count;i++){float expected=doorData.Positions[i]+(i%3==0?BuildingCatalog.Get(0).HingeX:i%3==2?BuildingCatalog.Get(0).HingeZ:0);if(combined.Positions[before.Positions.Count+i]!=expected)throw new InvalidDataException("Merged door lost its hinge pivot");}
            Console.WriteLine("Building checks passed: source confinement, explicit IDs, collision frame, sockets, activity/fixture bounds, exit clearance and triangle budgets");
        }finally{Directory.Delete(temp,true);Cook(root);}
    }
    private static void MergeClosed(string exterior,string door,string output,float hingeX,float hingeZ)
    {
        // Match the 1/1024 m sockets written to buildings.bin and used by
        // the animated door, including the closed-to-opening handoff.
        hingeX=(int)(hingeX*1024)/1024f;hingeZ=(int)(hingeZ*1024)/1024f;
        var body=ReadMergePart(exterior,out byte[] atlas,out var template);
        var leaf=ReadMergePart(door,out byte[] doorAtlas,out var doorTemplate);
        foreach(string key in new[]{"materials","textures","samplers"})
            if(!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(template.GetProperty(key).GetRawText()),System.Text.Json.Nodes.JsonNode.Parse(doorTemplate.GetProperty(key).GetRawText())))throw new InvalidDataException("Closed building material mismatch: "+key);
        if(!atlas.AsSpan().SequenceEqual(doorAtlas))throw new InvalidDataException("Closed building atlas mismatch");
        for(int i=0;i<leaf.Positions.Count;i+=3){leaf.Positions[i]+=hingeX;leaf.Positions[i+2]+=hingeZ;}
        body.Positions.AddRange(leaf.Positions);body.Normals.AddRange(leaf.Normals);body.Uvs.AddRange(leaf.Uvs);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);Write(output,body,atlas,template);Audit(output,"exterior");
    }
    // Narrow, validated cooker for this static kit. Unsupported source layouts
    // fail the build instead of silently dropping a node transform or material.
    private static MeshData ReadMergePart(string path,out byte[] png,out JsonElement template)
    {
        byte[] bytes=File.ReadAllBytes(path);using var doc=JsonDocument.Parse(bytes.AsMemory(20,BitConverter.ToInt32(bytes,12)));var root=doc.RootElement;template=root.Clone();
        if(root.GetProperty("meshes").GetArrayLength()!=1||root.GetProperty("nodes").GetArrayLength()!=1||root.GetProperty("materials").GetArrayLength()!=1||root.GetProperty("images").GetArrayLength()!=1)throw new InvalidDataException("Closed building requires one static mesh/material/atlas");
        var node=root.GetProperty("nodes")[0];foreach(var property in node.EnumerateObject())if(property.Name!="mesh"&&property.Name!="name")throw new InvalidDataException("Closed building node transform/extension is unsupported");
        var primitives=root.GetProperty("meshes")[0].GetProperty("primitives");if(primitives.GetArrayLength()!=1)throw new InvalidDataException("Closed building requires one primitive");var primitive=primitives[0];
        foreach(var property in primitive.EnumerateObject())if(property.Name!="attributes"&&property.Name!="material"&&property.Name!="mode")throw new InvalidDataException("Closed building primitive extension/index layout is unsupported");
        if(primitive.GetProperty("material").GetInt32()!=0||primitive.GetProperty("mode").GetInt32()!=4)throw new InvalidDataException("Closed building requires triangles");
        int bin=28+BitConverter.ToInt32(bytes,12);var mesh=new MeshData();var attrs=primitive.GetProperty("attributes");
        foreach(var property in attrs.EnumerateObject())if(property.Name!="POSITION"&&property.Name!="NORMAL"&&property.Name!="TEXCOORD_0")throw new InvalidDataException("Closed building vertex attribute unsupported");
        Read("POSITION",3,mesh.Positions);Read("NORMAL",3,mesh.Normals);Read("TEXCOORD_0",2,mesh.Uvs);
        if(mesh.Positions.Count!=mesh.Normals.Count||mesh.Positions.Count/3*2!=mesh.Uvs.Count||mesh.Positions.Count%9!=0)throw new InvalidDataException("Closed building attribute counts");
        var image=root.GetProperty("bufferViews")[root.GetProperty("images")[0].GetProperty("bufferView").GetInt32()];png=bytes.AsSpan(bin+image.GetProperty("byteOffset").GetInt32(),image.GetProperty("byteLength").GetInt32()).ToArray();return mesh;
        void Read(string name,int width,List<float> output){
            var accessor=root.GetProperty("accessors")[attrs.GetProperty(name).GetInt32()];var view=root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            if(accessor.GetProperty("componentType").GetInt32()!=5126||accessor.GetProperty("type").GetString()!=(width==3?"VEC3":"VEC2")||accessor.TryGetProperty("sparse",out _)||view.GetProperty("buffer").GetInt32()!=0)throw new InvalidDataException("Closed building accessor unsupported");
            int offset=accessor.TryGetProperty("byteOffset",out var o)?o.GetInt32():0,stride=view.TryGetProperty("byteStride",out var st)?st.GetInt32():width*4,count=accessor.GetProperty("count").GetInt32();
            if(count<1||stride<width*4||offset<0||(long)offset+(long)(count-1)*stride+width*4>view.GetProperty("byteLength").GetInt32())throw new InvalidDataException("Closed building accessor bounds");
            int start=bin+view.GetProperty("byteOffset").GetInt32()+offset;
            for(int i=0;i<count;i++)for(int axis=0;axis<width;axis++){float value=BitConverter.ToSingle(bytes,start+i*stride+axis*4);if(!float.IsFinite(value))throw new InvalidDataException("Closed building non-finite vertex");output.Add(value);}
        }
    }
    private static void ValidateName(string name){if(name.Length<1||name.Length>64||name.Any(c=>!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='_'||c=='-')))throw new InvalidDataException("Invalid asset identifier");}
    private static void Audit(string path,string part)
    {
        byte[] b=File.ReadAllBytes(path);if(b.Length<20||b.Length>512*1024||BitConverter.ToUInt32(b)!=0x46546c67)throw new InvalidDataException("Invalid or oversized building GLB");using var doc=JsonDocument.Parse(b.AsMemory(20,BitConverter.ToInt32(b,12)));var root=doc.RootElement;if(root.TryGetProperty("skins",out _)||root.TryGetProperty("animations",out _))throw new InvalidDataException("Building parts must be static");int triangles=0;var accessors=root.GetProperty("accessors");foreach(var mesh in root.GetProperty("meshes").EnumerateArray())foreach(var primitive in mesh.GetProperty("primitives").EnumerateArray()){int count=primitive.TryGetProperty("indices",out var i)?accessors[i.GetInt32()].GetProperty("count").GetInt32():accessors[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32();triangles+=count/3;
            var position=accessors[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];var min=position.GetProperty("min");var max=position.GetProperty("max");
            float edge=part=="exterior"?3.3f:part=="door"?1.2f:4.2f,height=part=="exterior"?4:3;
            for(int axis=0;axis<3;axis++){float lo=min[axis].GetSingle(),hi=max[axis].GetSingle();if(!float.IsFinite(lo+hi)||lo<-.3f||hi>(axis==1?height:part=="exterior"&&axis==2?4:edge)||lo>hi)throw new InvalidDataException("Building part exceeds its collision/socket frame");}
            if(!primitive.GetProperty("attributes").TryGetProperty("NORMAL",out _)||!primitive.GetProperty("attributes").TryGetProperty("TEXCOORD_0",out _))throw new InvalidDataException("Building normals and UVs required");
        }
        if(triangles<1||triangles>(part=="exterior"?800:600)||root.GetProperty("materials").GetArrayLength()>2)throw new InvalidDataException("Building geometry/material budget");
    }
    private sealed class MeshData
    {
        internal readonly List<float> Positions=new(),Normals=new(),Uvs=new();
        internal void Triangle(Vector3 a,Vector3 b,Vector3 c,int swatch){Vector3 n=Vector3.Normalize(Vector3.Cross(b-a,c-a));if(!float.IsFinite(n.X))throw new InvalidDataException("Degenerate building face");int i=0;foreach(var p in new[]{a,b,c}){Positions.AddRange(new[]{p.X,p.Y,p.Z});Normals.AddRange(new[]{n.X,n.Y,n.Z});float u=(swatch%4+.1f+(i==1?.8f:0))/4,v=(swatch/4+.1f+(i==2?.8f:0))/4;Uvs.Add(u);Uvs.Add(v);i++;}}
        internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int swatch){Triangle(a,b,c,swatch);Triangle(a,c,d,swatch);}
        internal void Box(float x,float y,float z,float w,float h,float d,int swatch){Vector3 a=new(x,y,z),b=new(x+w,y,z),c=new(x+w,y+h,z),e=new(x,y+h,z),f=new(x,y,z+d),g=new(x+w,y,z+d),j=new(x+w,y+h,z+d),k=new(x,y+h,z+d);Quad(a,e,c,b,swatch);Quad(f,g,j,k,swatch);Quad(a,f,k,e,swatch);Quad(b,c,j,g,swatch);Quad(e,k,j,c,swatch);Quad(a,b,g,f,swatch);}
    }
    private static void Write(string path,MeshData mesh,byte[] png,JsonElement? template=null)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);foreach(float f in mesh.Positions)writer.Write(f);int normal=(int)stream.Position;foreach(float f in mesh.Normals)writer.Write(f);int uv=(int)stream.Position;foreach(float f in mesh.Uvs)writer.Write(f);int image=(int)stream.Position;writer.Write(png);while(stream.Length%4!=0)writer.Write((byte)0);
        int count=mesh.Positions.Count/3;float[] min={float.MaxValue,float.MaxValue,float.MaxValue},max={float.MinValue,float.MinValue,float.MinValue};for(int i=0;i<count;i++)for(int axis=0;axis<3;axis++){min[axis]=Math.Min(min[axis],mesh.Positions[i*3+axis]);max[axis]=Math.Max(max[axis],mesh.Positions[i*3+axis]);}
        var gltf=new{asset=new{version="2.0",generator="AnimalTown original cottage kit"},buffers=new[]{new{byteLength=stream.Length}},bufferViews=new[]{new{buffer=0,byteOffset=0,byteLength=normal},new{buffer=0,byteOffset=normal,byteLength=uv-normal},new{buffer=0,byteOffset=uv,byteLength=image-uv},new{buffer=0,byteOffset=image,byteLength=png.Length}},accessors=new object[]{new{bufferView=0,componentType=5126,count,type="VEC3",min,max},new{bufferView=1,componentType=5126,count,type="VEC3"},new{bufferView=2,componentType=5126,count,type="VEC2"}},images=new[]{new{bufferView=3,mimeType="image/png"}},samplers=new[]{new{magFilter=9729,minFilter=9987,wrapS=33071,wrapT=33071}},textures=new[]{new{sampler=0,source=0}},materials=new[]{new{name="Shared cottage palette",pbrMetallicRoughness=new{baseColorTexture=new{index=0},metallicFactor=0,roughnessFactor=1}}},meshes=new[]{new{primitives=new[]{new{attributes=new{POSITION=0,NORMAL=1,TEXCOORD_0=2},material=0,mode=4}}}},nodes=new[]{new{mesh=0,name=Path.GetFileNameWithoutExtension(path)}},scenes=new[]{new{nodes=new[]{0}}},scene=0};
        var document=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(gltf))!;
        if(template.HasValue)foreach(string key in new[]{"materials","textures","samplers"})document[key]=System.Text.Json.Nodes.JsonNode.Parse(template.Value.GetProperty(key).GetRawText());
        byte[] json=Encoding.UTF8.GetBytes(document.ToJsonString());int padded=(json.Length+3)&~3;using var output=File.Create(path);using var dst=new BinaryWriter(output);dst.Write(0x46546c67);dst.Write(2);dst.Write(12+8+padded+8+(int)stream.Length);dst.Write(padded);dst.Write(0x4e4f534a);dst.Write(json);for(int i=json.Length;i<padded;i++)dst.Write((byte)32);dst.Write((int)stream.Length);dst.Write(0x004e4942);dst.Write(stream.ToArray());
    }
}
