using System.Buffers.Binary;
using System.Text.Json.Nodes;

internal static class TerrainPrimitiveAssets
{
    internal static void Build(string root)
    {
        string folder=Path.Combine(root,"assets/models/terrain");Directory.CreateDirectory(folder);
        Write(Path.Combine(folder,"section_cube_2m.glb"),false);Write(Path.Combine(folder,"section_ramp_2m.glb"),true);
        Console.WriteLine("Terrain prototypes: 2x2x2m cube (12 triangles), 2m single-section ramp (8 triangles)");
    }
    private static void Write(string path,bool ramp)
    {
        float[] p=ramp?new float[]{0,0,0,2,0,0,2,0,2,0,0,2,0,2,0,2,2,0}:new float[]{0,0,0,2,0,0,2,0,2,0,0,2,0,2,0,2,2,0,2,2,2,0,2,2};
        int[] triangles=ramp?new[]{0,1,2,0,2,3,4,2,5,4,3,2,0,5,1,0,4,5,0,3,4,1,5,2}:new[]{0,1,2,0,2,3,4,6,5,4,7,6,0,5,1,0,4,5,1,6,2,1,5,6,2,7,3,2,6,7,3,4,0,3,7,4};
        float volume=0;for(int t=0;t<triangles.Length;t+=3){int a=triangles[t]*3,b=triangles[t+1]*3,c=triangles[t+2]*3;volume+=p[a]*(p[b+1]*p[c+2]-p[b+2]*p[c+1])+p[a+1]*(p[b+2]*p[c]-p[b]*p[c+2])+p[a+2]*(p[b]*p[c+1]-p[b+1]*p[c]);}
        if(Math.Abs(volume/6-(ramp?4:8))>.001f)throw new InvalidDataException("Terrain prototype must be closed with outward triangle winding");
        int vertexBytes=p.Length*4;byte[] bin=new byte[vertexBytes+triangles.Length*2];for(int i=0;i<p.Length;i++)BitConverter.GetBytes(p[i]).CopyTo(bin,i*4);for(int i=0;i<triangles.Length;i++)BinaryPrimitives.WriteUInt16LittleEndian(bin.AsSpan(vertexBytes+i*2),(ushort)triangles[i]);
        var json=JsonNode.Parse("""{"asset":{"version":"2.0","generator":"AnimalTown terrain prototypes"},"buffers":[{}],"bufferViews":[{},{}],"accessors":[{"componentType":5126,"type":"VEC3","bufferView":0,"min":[0,0,0],"max":[2,2,2]},{"componentType":5123,"type":"SCALAR","bufferView":1}],"materials":[{"name":"Prototype earth","pbrMetallicRoughness":{"baseColorFactor":[0.54,0.47,0.34,1],"metallicFactor":0,"roughnessFactor":1}}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":0}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}""")!.AsObject();
        json["buffers"]![0]!["byteLength"]=bin.Length;json["bufferViews"]![0]=new JsonObject{["buffer"]=0,["byteOffset"]=0,["byteLength"]=vertexBytes,["target"]=34962};json["bufferViews"]![1]=new JsonObject{["buffer"]=0,["byteOffset"]=vertexBytes,["byteLength"]=triangles.Length*2,["target"]=34963};json["accessors"]![0]!["count"]=p.Length/3;json["accessors"]![1]!["count"]=triangles.Length;
        File.WriteAllBytes(path,TextureRepacker.EncodeGlb(json,bin));
    }
}
