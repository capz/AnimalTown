using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimalTown;

// Read the retired assets directly from a reference ISO; never stage/load them
// in the active game. Counts describe submitted work, not visible pixels.
internal static class TreeRenderAudit
{
    internal static void Run(string root,string isoPath)
    {
        using var iso=File.OpenRead(isoPath);
        byte[] Read(long offset,int count)
        {
            if(offset<0||count<0||offset>iso.Length-count)throw new InvalidDataException("ISO range outside file");
            var bytes=new byte[count];iso.Position=offset;iso.ReadExactly(bytes);return bytes;
        }
        int U(byte[] bytes,int at)=>checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at,4)));
        var boot=Read(0,0x430);
        if(BinaryPrimitives.ReadUInt32BigEndian(boot.AsSpan(0x1c,4))!=0xc2339f3d)throw new InvalidDataException("Not a GameCube disc");
        int fstLength=U(boot,0x428);if(fstLength<12||fstLength>1024*1024)throw new InvalidDataException("Invalid FST length");
        var fst=Read(U(boot,0x424),fstLength);int files=U(fst,8);
        if(files<2||files>1024||files*12>fst.Length)throw new InvalidDataException("Invalid FST root");
        var originals=new List<object>();
        for(int file=1;file<files;file++)
        {
            int nameAt=files*12+(U(fst,file*12)&0xffffff);
            if(nameAt<files*12||nameAt>=fst.Length)throw new InvalidDataException("Invalid FST name");
            int end=Array.IndexOf(fst,(byte)0,nameAt);if(end<0)throw new InvalidDataException("Unterminated FST name");
            string name=Encoding.ASCII.GetString(fst,nameAt,end-nameAt);
            if(name!="deciduous.dndmodel"&&name!="palm.dndmodel"&&name!="pine.dndmodel")continue;
            int size=U(fst,file*12+8);if(size<160||size>16*1024*1024)throw new InvalidDataException("Invalid model size");
            var bytes=Read(U(fst,file*12+4),size);
            if(U(bytes,0)!=0x444e444d||U(bytes,8)!=size||U(bytes,12)!=6)throw new InvalidDataException("Invalid model header");
            int version=U(bytes,4);if(version<1||version>2)throw new InvalidDataException("Unsupported model version");
            int Section(int index)
            {
                int at=U(bytes,20+index*16),length=U(bytes,24+index*16);
                if(U(bytes,16+index*16)!=index+1||at<160||length<4||at>size-length)throw new InvalidDataException("Invalid model section");
                return at;
            }
            int meshAt=Section(1),meshCount=U(bytes,meshAt);meshAt+=4;
            int vertices=0,corners=0,skinnedMeshes=0;var meshMaterials=new List<(int material,int corners)>();
            for(int i=0;i<meshCount;i++)
            {
                int skin=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(meshAt+4,4));
                int vertexCount=U(bytes,meshAt+12),indexCount=U(bytes,meshAt+16);
                meshMaterials.Add((U(bytes,meshAt+8),indexCount));
                vertices+=vertexCount;corners+=indexCount;if(skin>=0)skinnedMeshes++;
                meshAt=checked(meshAt+44+vertexCount*80+indexCount*4);
                if(meshAt>size)throw new InvalidDataException("Invalid mesh payload");
            }
            int matAt=Section(2),materialCount=U(bytes,matAt);matAt+=4;
            var materials=new List<object>();var modes=new int[materialCount];
            for(int i=0;i<materialCount;i++,matAt+=version==2?40:32)
            {
                int mode=U(bytes,matAt+20);modes[i]=mode;
                int usedCorners=0;foreach(var mesh in meshMaterials)if(mesh.material==i)usedCorners+=mesh.corners;
                materials.Add(new{index=i,triangleCorners=usedCorners,unlit=(mode&2)!=0,blended=(mode&4)!=0,alphaTested=(mode&1)!=0,twoSided=U(bytes,matAt+28)!=0});
            }
            int textureAt=Section(3),textureCount=U(bytes,textureAt);textureAt+=4;
            var textures=new List<object>();int textureBytes=0;
            for(int i=0;i<textureCount;i++)
            {
                int width=U(bytes,textureAt),height=U(bytes,textureAt+4),levels=U(bytes,textureAt+8),payload=U(bytes,textureAt+24);
                textureAt+=28;textureBytes+=payload;
                textures.Add(new{width,height,levels,format="GX_TF_RGBA8",residentBytes=payload,sha256=Convert.ToHexString(SHA256.HashData(bytes.AsSpan(textureAt,payload)))});
                textureAt=checked(textureAt+payload);
            }
            int normalizations=0;foreach(var mesh in meshMaterials)if((modes[mesh.material]&2)==0)normalizations+=mesh.corners;
            originals.Add(new{source="dvd:/"+name,packageSha256=Convert.ToHexString(SHA256.HashData(bytes)),packageBytes=size,vertices,triangles=corners/3,triangleCorners=corners,skinnedMeshes,meshCount,materials,textures,textureBytes,
                // Direct GX: XYZ floats + RGBA8 + UV floats. Excludes GX commands,
                // state and texture traffic; this is not a total bandwidth estimate.
                directVertexPayloadBytes=corners*24,
                fallbackNormalNormalizationsPerDraw=normalizations});
        }
        if(originals.Count!=3)throw new InvalidDataException("Reference ISO must contain all three retired tree models");
        iso.Position=0;string isoSha=Convert.ToHexString(SHA256.HashData(iso));
        var generated=new List<object>();
        for(int kind=0;kind<3;kind++){
            var tree=LowPolyTree.Build(kind);generated.Add(new{kind,triangles=tree.Triangles,parts=4,textures=0,indexedPositionPayloadBytes=tree.Triangles*3*2});
        }
        var report=new{referenceIsoSha256=isoSha,originals,generated,
            note="Static asset/submission audit, not isolated timings. Old counts assume the default lit imported-model path; generated counts assume the uniform-color unlit indexed path."};
        Directory.CreateDirectory(Path.Combine(root,"out"));string output=Path.Combine(root,"out/tree-render-audit.json");
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(output);
    }
}
