using System.Buffers.Binary;
using System.Numerics;

internal static class ModelLodChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new InvalidDataException("LOD check: "+message);}
    private static int Int(byte[] bytes,int at)=>BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at,4));
    private static void Put(byte[] bytes,int at,int value)=>BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at,4),value);
    private static void Float(byte[] bytes,int at,float value)=>Put(bytes,at,BitConverter.SingleToInt32Bits(value));
    private static byte[] Section(byte[] package,int index){int directory=16+index*16;return package.AsSpan(Int(package,directory+4),Int(package,directory+8)).ToArray();}
    internal static byte[] Grid(bool splitUv=false,bool splitSkin=false,float scale=1)
    {
        const int side=9,count=side*side;byte[] nodes=new byte[112];Put(nodes,0,1);Put(nodes,4,-1);Float(nodes,4+68+24,1);for(int i=0;i<3;i++)Float(nodes,4+68+28+i*4,scale);
        byte[] mesh=new byte[4+44+count*80+(side-1)*(side-1)*6*4];Put(mesh,0,1);Put(mesh,4,0);Put(mesh,8,splitSkin?0:-1);Put(mesh,12,0);Put(mesh,16,count);Put(mesh,20,(side-1)*(side-1)*6);Float(mesh,36,.024f/scale);Float(mesh,40,.024f/scale);
        for(int y=0;y<side;y++)for(int x=0;x<side;x++){
            int at=48+(y*side+x)*80;Float(mesh,at,x*.003f/scale);Float(mesh,at+4,y*.003f/scale);Float(mesh,at+20,1);
            if(splitUv)Float(mesh,at+24,(x+y)%2);
            for(int i=0;i<4;i++)Float(mesh,at+32+i*4,1);
            if(splitSkin){Put(mesh,at+48,(x+y)%2);Float(mesh,at+64,1);}
        }
        int cursor=48+count*80;
        for(int y=0;y<side-1;y++)for(int x=0;x<side-1;x++)foreach(int index in new[]{y*side+x,y*side+x+1,(y+1)*side+x,y*side+x+1,(y+1)*side+x+1,(y+1)*side+x}){Put(mesh,cursor,index);cursor+=4;}
        byte[] skins=new byte[splitSkin?4+4+2*68:4];if(splitSkin){Put(skins,0,1);Put(skins,4,2);for(int j=0;j<2;j++)for(int i=0;i<4;i++)Float(skins,8+j*68+4+(i*4+i)*4,1);}
        return ModelLodCooker.Package(new[]{nodes,mesh,new byte[]{0,0,0,1},new byte[4],skins,new byte[4]},2);
    }
    private static (int euler,int boundary) Topology(byte[] package)
    {
        byte[] mesh=Section(package,1);int count=Int(mesh,16),triangles=Int(mesh,20)/3,at=48+count*80;var edges=new Dictionary<(int,int),int>();var used=new HashSet<int>();var faces=new HashSet<(int,int,int)>();
        var weld=new int[count];var positions=new Dictionary<string,int>();for(int i=0;i<count;i++){string key=Convert.ToHexString(mesh.AsSpan(48+i*80,12));if(!positions.TryGetValue(key,out int id)){id=i;positions.Add(key,id);}weld[i]=id;}
        for(int i=0;i<triangles;i++){
            int[] ids={Int(mesh,at+i*12),Int(mesh,at+i*12+4),Int(mesh,at+i*12+8)};Check(ids.All(v=>(uint)v<count)&&ids.Distinct().Count()==3,"valid triangle topology");for(int k=0;k<3;k++)ids[k]=weld[ids[k]];Check(ids.Distinct().Count()==3,"valid welded topology");
            foreach(int v in ids)used.Add(v);int[] sorted=(int[])ids.Clone();Array.Sort(sorted);Check(faces.Add((sorted[0],sorted[1],sorted[2])),"no duplicate faces");
            for(int j=0;j<3;j++){int a=ids[j],b=ids[(j+1)%3];if(a>b)(a,b)=(b,a);edges[(a,b)]=edges.GetValueOrDefault((a,b))+1;}
        }
        Check(edges.Values.All(n=>n is 1 or 2),"manifold edge counts");return(used.Count-edges.Count+triangles,edges.Values.Count(n=>n==1));
    }
    private static byte[] SeamGrid()
    {
        byte[] source=Grid(),mesh=Section(source,1);const int original=81,added=9;int indices=Int(mesh,20),indexAt=48+original*80;byte[] split=new byte[48+(original+added)*80+indices*4];mesh.AsSpan(0,indexAt).CopyTo(split);Put(split,16,original+added);
        for(int y=0;y<9;y++){mesh.AsSpan(48+(y*9+4)*80,80).CopyTo(split.AsSpan(48+(original+y)*80));Float(split,48+(original+y)*80+24,.5f);}
        for(int t=0;t<indices;t+=3){int[] triangle={Int(mesh,indexAt+t*4),Int(mesh,indexAt+(t+1)*4),Int(mesh,indexAt+(t+2)*4)};bool right=triangle.Any(v=>v%9>4);for(int i=0;i<3;i++){int v=triangle[i];if(right&&v%9==4)v=original+v/9;Put(split,48+(original+added)*80+(t+i)*4,v);}}
        var sections=Enumerable.Range(0,6).Select(i=>Section(source,i)).ToArray();sections[1]=split;return ModelLodCooker.Package(sections,2);
    }
    internal static void Run()
    {
        byte[] source=Grid(),lod=ModelLodCooker.Simplify(source,out var report,out int[][] map),repeat=ModelLodCooker.Simplify(source,out _,out _);
        Check(lod.SequenceEqual(repeat),"deterministic result");Check(report.Collapses>0&&report.Triangles<report.SourceTriangles&&report.Vertices<report.SourceVertices,"actual vertex and triangle reduction");
        Check(report.MaximumNormalizedBindError<=ModelLodCooker.MaximumBindError,"bounded cumulative displacement");Check(report.MaximumSkinWeightDelta<=ModelLodCooker.MaximumWeightDelta,"bounded cumulative skin change");
        foreach(int index in new[]{0,2,3,4,5})Check(Section(source,index).SequenceEqual(Section(lod,index)),"non-geometry sections preserved");
        byte[] originalMesh=Section(source,1),lodMesh=Section(lod,1);Check(originalMesh.AsSpan(24,24).SequenceEqual(lodMesh.AsSpan(24,24)),"original conservative bounds preserved");
        for(int i=0;i<report.Vertices;i++){bool found=false;for(int j=0;j<report.SourceVertices;j++)if(lodMesh.AsSpan(48+i*80,80).SequenceEqual(originalMesh.AsSpan(48+j*80,80)))found=true;Check(found,"retained attribute records are byte-exact");}
        for(int i=0;i<map[0].Length;i++)Check(map[0][i]>=0&&map[0][i]<report.Vertices,"source-to-LOD correspondence valid");
        Check(Topology(source)==Topology(lod),"Euler characteristic and boundary edges unchanged");
        byte[] seamSource=SeamGrid(),seamLod=ModelLodCooker.Simplify(seamSource,out var seamReport,out _);Check(seamReport.Collapses>0&&Topology(seamSource)==Topology(seamLod),"seam copies collapse atomically without cracks or changed topology");
        ModelLodCooker.Simplify(Grid(scale:.01f),out var scaled,out _);Check(scaled.Collapses==report.Collapses&&Math.Abs(scaled.MaximumNormalizedBindError-report.MaximumNormalizedBindError)<1e-6f,"error uses normalized scene, not raw vertex units");
        ModelLodCooker.Simplify(Grid(splitSkin:true),out var skin,out _);Check(skin.MaximumSkinWeightDelta<=ModelLodCooker.MaximumWeightDelta,"skin discontinuities protected");
        ModelLodCooker.Simplify(Grid(splitUv:true),out var uv,out _);Check(uv.Triangles>=report.Triangles,"texture discontinuities constrain simplification");
        byte[] corrupt=(byte[])source.Clone();corrupt[Int(source,36)+4]^=1;bool rejected=false;try{ModelLodCooker.Simplify(corrupt,out _,out _);}catch(InvalidDataException){rejected=true;}Check(rejected,"damaged package rejected");
        Console.WriteLine("Model LOD checks passed: topology, normalized error, exact attributes/sections, seams, skin discontinuities and determinism");
    }
}
