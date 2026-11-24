using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

// Offline endpoint collapses keep the exact source attributes at surviving
// vertices. Boundary/seam vertices, manifold connectivity and face orientation
// are protected; no runtime simplification or triangle deletion heuristic.
internal static class ModelLodCooker
{
    internal const float MaximumBindError=.04f,MaximumWeightDelta=.1f;
    private const float MaximumUvDelta=.04f;
    internal static readonly string[] Species={"fox","bunny","giraffe","cat","hamster","spaniel"};
    internal sealed record Result(int SourceVertices,int Vertices,int SourceTriangles,int Triangles,float MaximumNormalizedBindError,float MaximumSkinWeightDelta,int Collapses);
    private sealed class Face
    {
        internal int A,B,C;internal bool Live=true;internal Vector3 Normal;
        internal Face(int a,int b,int c,Vector3[] positions){A=a;B=b;C=c;Normal=Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]);}
        internal int[] Vertices()=>new[]{A,B,C};
    }
    private readonly record struct Edge(int A,int B)
    {
        internal static Edge Of(int a,int b)=>a<b?new(a,b):new(b,a);
    }
    private sealed class Mesh
    {
        internal required byte[] Header,Data;internal required int[] Indices;internal required Vector3[] Positions;
    }
    internal static void CookAll(string root,float maximumBindError=MaximumBindError)
    {
        var reports=new List<object>();
        foreach(string species in Species){
            string source=Path.Combine(root,"out/disc",species+"-tex128.dndmodel"),destination=Path.Combine(root,"out/disc",species+"-lod1-tex128.dndmodel");
            byte[] original=File.ReadAllBytes(source),lod=Simplify(original,out Result report,out int[][] correspondence,maximumBindError);
            if(report.Collapses==0||report.Triangles>=report.SourceTriangles||report.Vertices>=report.SourceVertices)throw new InvalidDataException("No safe geometry reduction for "+species);
            bool runtimeEligible=AnimalTown.VillagerLod.CoarseAvailable(Array.IndexOf(Species,species));
            if(runtimeEligible&&maximumBindError==MaximumBindError&&report.Triangles>report.SourceTriangles*.8f)throw new InvalidDataException("Runtime LOD must save at least 20% of triangles: "+species);
            File.WriteAllBytes(destination,lod);
            // The native pose fixture consumes exact original -> compacted LOD
            // correspondence, outside the shipped runtime payload.
            string map=Path.Combine(root,"out",species+"-lod1-map.bin");
            using(var stream=File.Create(map)){Write(stream,correspondence.Length);foreach(int[] mesh in correspondence){Write(stream,mesh.Length);foreach(int vertex in mesh)Write(stream,vertex);}}
            reports.Add(new{species,runtimeEligible,report,sourceBytes=original.Length,lodBytes=lod.Length,sourceSha256=Hash(original),lodSha256=Hash(lod),errorSpace="normalized bind pose; animated endpoint error measured separately",retainedVertexAttributes="byte-exact",otherSections="byte-identical"});
            Console.WriteLine($"LOD {species}: {report.SourceTriangles} -> {report.Triangles} triangles, {report.SourceVertices} -> {report.Vertices} vertices; bind error {report.MaximumNormalizedBindError:G6}, weight delta {report.MaximumSkinWeightDelta:G6}");
        }
        File.WriteAllText(Path.Combine(root,"out/model-lod-audit.json"),JsonSerializer.Serialize(new{schema=1,maximumBindError,maximumWeightDelta=MaximumWeightDelta,models=reports},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static uint Fnv(ReadOnlySpan<byte> bytes){uint value=2166136261;foreach(byte b in bytes)value=unchecked((value^b)*16777619);return value;}
    private static int Int(byte[] bytes,int at)=>BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at,4));
    private static float Float(byte[] bytes,int at){float value=BitConverter.Int32BitsToSingle(Int(bytes,at));return float.IsFinite(value)?value:throw new InvalidDataException("Nonfinite LOD scalar");}
    private static void Put(byte[] bytes,int at,int value)=>BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at,4),value);
    private static void Write(Stream stream,int value){Span<byte> bytes=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(bytes,value);stream.Write(bytes);}
    private static Vector3 Vector(byte[] bytes,int at)=>new(Float(bytes,at),Float(bytes,at+4),Float(bytes,at+8));
    private static Matrix4x4 Matrix(byte[] bytes,int at)=>new(Float(bytes,at),Float(bytes,at+4),Float(bytes,at+8),Float(bytes,at+12),Float(bytes,at+16),Float(bytes,at+20),Float(bytes,at+24),Float(bytes,at+28),Float(bytes,at+32),Float(bytes,at+36),Float(bytes,at+40),Float(bytes,at+44),Float(bytes,at+48),Float(bytes,at+52),Float(bytes,at+56),Float(bytes,at+60));
    private static byte[][] Sections(byte[] bytes)
    {
        if(bytes.Length<160||Int(bytes,0)!=0x444e444d||Int(bytes,4) is not (1 or 2)||Int(bytes,8)!=bytes.Length||Int(bytes,12)!=6)throw new InvalidDataException("Invalid LOD package header");
        var sections=new byte[6][];int previous=160;
        for(int i=0;i<6;i++){
            int directory=16+i*16,start=Int(bytes,directory+4),length=Int(bytes,directory+8);
            if(Int(bytes,directory)!=i+1||start<previous||start%32!=0||length<4||start>bytes.Length-length)throw new InvalidDataException("Invalid LOD package section");
            sections[i]=bytes.AsSpan(start,length).ToArray();previous=start+length;
            if(Fnv(sections[i])!=unchecked((uint)Int(bytes,directory+12)))throw new InvalidDataException("LOD package checksum mismatch");
        }
        return sections;
    }
    internal static byte[] Package(byte[][] sections,int version=1)
    {
        int size=160;foreach(byte[] section in sections)size=checked((size+section.Length+31)&~31);
        var bytes=new byte[size];Put(bytes,0,0x444e444d);Put(bytes,4,version);Put(bytes,8,size);Put(bytes,12,6);int at=160;
        for(int i=0;i<6;i++){int directory=16+i*16;Put(bytes,directory,i+1);Put(bytes,directory+4,at);Put(bytes,directory+8,sections[i].Length);Put(bytes,directory+12,unchecked((int)Fnv(sections[i])));sections[i].CopyTo(bytes,at);at=(at+sections[i].Length+31)&~31;}
        return bytes;
    }
    private static Matrix4x4[] NodeMatrices(byte[] nodes)
    {
        int count=Int(nodes,0);if(count<1||count>1024||nodes.Length!=4+count*108)throw new InvalidDataException("Invalid LOD nodes");
        var local=new Matrix4x4[count];var global=new Matrix4x4[count];var done=new byte[count];var parent=new int[count];
        for(int i=0;i<count;i++){
            int at=4+i*108;parent[i]=Int(nodes,at);if(parent[i]< -1||parent[i]>=count||parent[i]==i)throw new InvalidDataException("Invalid LOD parent");
            at+=68;Vector3 t=Vector(nodes,at),s=Vector(nodes,at+28);var q=new Quaternion(Float(nodes,at+12),Float(nodes,at+16),Float(nodes,at+20),Float(nodes,at+24));
            local[i]=Matrix4x4.CreateScale(s)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(t);
        }
        Matrix4x4 Visit(int i){if(done[i]==2)return global[i];if(done[i]==1)throw new InvalidDataException("LOD node cycle");done[i]=1;global[i]=parent[i]<0?local[i]:local[i]*Visit(parent[i]);done[i]=2;return global[i];}
        for(int i=0;i<count;i++)Visit(i);return global;
    }
    private static Matrix4x4[][] SkinMatrices(byte[] skins,Matrix4x4[] nodes)
    {
        int count=Int(skins,0),at=4;if(count<0||count>1024)throw new InvalidDataException("Invalid LOD skins");var matrices=new Matrix4x4[count][];
        for(int skin=0;skin<count;skin++){
            if(at>skins.Length-4)throw new InvalidDataException("Truncated LOD skin");int joints=Int(skins,at);at+=4;
            if(joints<1||joints>256||at>skins.Length-joints*68)throw new InvalidDataException("Invalid LOD skin joints");matrices[skin]=new Matrix4x4[joints];
            for(int joint=0;joint<joints;joint++){int node=Int(skins,at);if((uint)node>=nodes.Length)throw new InvalidDataException("Invalid LOD skin node");matrices[skin][joint]=Matrix(skins,at+4)*nodes[node];at+=68;}
        }
        if(at!=skins.Length)throw new InvalidDataException("LOD skin trailing bytes");return matrices;
    }
    internal static byte[] Simplify(byte[] original,out Result result,out int[][] correspondence,float maximumBindError=MaximumBindError)
    {
        if(!float.IsFinite(maximumBindError)||maximumBindError<=0||maximumBindError>.05f)throw new ArgumentOutOfRangeException(nameof(maximumBindError));
        byte[][] sections=Sections(original);Matrix4x4[] nodes=NodeMatrices(sections[0]);Matrix4x4[][] skins=SkinMatrices(sections[4],nodes);
        byte[] source=sections[1];int count=Int(source,0),at=4;if(count<1||count>4096)throw new InvalidDataException("Invalid LOD mesh count");
        using var output=new MemoryStream();Write(output,count);correspondence=new int[count][];
        int sourceVertices=0,vertices=0,sourceTriangles=0,triangles=0,collapses=0;float error=0,weightError=0;
        for(int m=0;m<count;m++){
            if(at>source.Length-44)throw new InvalidDataException("Truncated LOD mesh");int node=Int(source,at),skin=Int(source,at+4),nv=Int(source,at+12),ni=Int(source,at+16);
            if((uint)node>=nodes.Length||skin< -1||skin>=skins.Length||nv<1||nv>65535||ni<3||ni>196605||ni%3!=0||at>source.Length-44-(long)nv*80-(long)ni*4)throw new InvalidDataException("Invalid LOD mesh range");
            byte[] data=source.AsSpan(at+44,nv*80).ToArray();int[] indices=new int[ni];int indexAt=at+44+nv*80;
            for(int i=0;i<ni;i++){indices[i]=Int(source,indexAt+i*4);if((uint)indices[i]>=nv)throw new InvalidDataException("Invalid LOD vertex index");}
            var positions=new Vector3[nv];
            for(int i=0;i<nv;i++){
                int v=i*80;Vector3 position=Vector(data,v);
                if(skin<0)positions[i]=Vector3.Transform(position,nodes[node]);
                else for(int j=0;j<4;j++){int joint=Int(data,v+48+j*4);float weight=Float(data,v+64+j*4);if((uint)joint>=skins[skin].Length||weight<0)throw new InvalidDataException("Invalid LOD vertex skin");positions[i]+=Vector3.Transform(position,skins[skin][joint])*weight;}
                if(!float.IsFinite(positions[i].X)||!float.IsFinite(positions[i].Y)||!float.IsFinite(positions[i].Z))throw new InvalidDataException("Invalid LOD normalized position");
            }
            var mesh=new Mesh{Header=source.AsSpan(at,44).ToArray(),Data=data,Indices=indices,Positions=positions};
            Result stats=Reduce(mesh,out byte[] cooked,out int[] map,maximumBindError);output.Write(cooked);correspondence[m]=map;
            sourceVertices+=stats.SourceVertices;vertices+=stats.Vertices;sourceTriangles+=stats.SourceTriangles;triangles+=stats.Triangles;collapses+=stats.Collapses;error=Math.Max(error,stats.MaximumNormalizedBindError);weightError=Math.Max(weightError,stats.MaximumSkinWeightDelta);
            at=indexAt+ni*4;
        }
        if(at!=source.Length)throw new InvalidDataException("LOD mesh trailing bytes");sections[1]=output.ToArray();byte[] reduced=Package(sections,Int(original,4));
        result=new(sourceVertices,vertices,sourceTriangles,triangles,error,weightError,collapses);return reduced;
    }
    private static Result Reduce(Mesh mesh,out byte[] cooked,out int[] correspondence,float maximumBindError)
    {
        int nv=mesh.Positions.Length;Vector3[] positions=mesh.Positions;byte[] data=mesh.Data;
        // A UV/normal seam has several attribute records at one geometric
        // vertex. Collapse all of them together, retaining the matching source
        // endpoint in each chart; treating them as independent boundaries would
        // either prevent useful reduction or open cracks between charts.
        var weld=new int[nv];var canonical=new Dictionary<Vector3,int>();
        for(int i=0;i<nv;i++){Vector3 point=Vector(data,i*80);if(!canonical.TryGetValue(point,out int id)){id=i;canonical.Add(point,id);}weld[i]=id;}
        var faces=new Face[mesh.Indices.Length/3];var incident=new HashSet<int>[nv];var locked=new bool[nv];var live=Enumerable.Repeat(true,nv).ToArray();var parents=Enumerable.Range(0,nv).ToArray();var groups=new List<int>[nv];
        var geometricParents=Enumerable.Range(0,nv).ToArray();var geometricOriginals=new List<int>[nv];var referenced=new bool[nv];
        for(int i=0;i<nv;i++){incident[i]=new();groups[i]=new(){i};geometricOriginals[i]=new();}
        for(int i=0;i<nv;i++)geometricOriginals[weld[i]].Add(i);
        int[] Geo(Face face)=>new[]{weld[face.A],weld[face.B],weld[face.C]};
        var edges=new Dictionary<Edge,int>();
        for(int f=0;f<faces.Length;f++){
            int at=f*3;Face face=faces[f]=new(mesh.Indices[at],mesh.Indices[at+1],mesh.Indices[at+2],positions);int[] ids=Geo(face);
            referenced[face.A]=referenced[face.B]=referenced[face.C]=true;
            foreach(int vertex in ids)incident[vertex].Add(f);
            foreach(Edge edge in new[]{Edge.Of(ids[0],ids[1]),Edge.Of(ids[1],ids[2]),Edge.Of(ids[2],ids[0])})edges[edge]=edges.GetValueOrDefault(edge)+1;
            if(ids.Distinct().Count()!=3||face.Normal.LengthSquared()<1e-20f)foreach(int vertex in ids)locked[vertex]=true;
        }
        foreach(var entry in edges)if(entry.Value!=2){locked[entry.Key.A]=locked[entry.Key.B]=true;}
        var queue=new PriorityQueue<Edge,(float,int,int)>();
        void Enqueue(Edge edge){if(edge.A!=edge.B&&live[edge.A]&&live[edge.B])queue.Enqueue(edge,(Vector3.DistanceSquared(positions[edge.A],positions[edge.B]),edge.A,edge.B));}
        foreach(Edge edge in edges.Keys)Enqueue(edge);
        HashSet<int> Neighbors(int vertex){var values=new HashSet<int>();foreach(int f in incident[vertex])if(faces[f].Live)foreach(int other in Geo(faces[f]))if(other!=vertex)values.Add(other);return values;}
        float SkinDelta(int a,int b){
            float total=0;Span<int> joints=stackalloc int[8];int count=0;
            for(int side=0;side<2;side++)for(int i=0;i<4;i++){
                int joint=Int(data,(side==0?a:b)*80+48+i*4);bool seen=false;for(int j=0;j<count;j++)if(joints[j]==joint)seen=true;if(seen)continue;joints[count++]=joint;
                float av=0,bv=0;for(int j=0;j<4;j++){if(Int(data,a*80+48+j*4)==joint)av+=Float(data,a*80+64+j*4);if(Int(data,b*80+48+j*4)==joint)bv+=Float(data,b*80+64+j*4);}total+=Math.Abs(av-bv);
            }
            return total;
        }
        bool Attributes(int keep,int drop){
            if(!data.AsSpan(keep*80+32,16).SequenceEqual(data.AsSpan(drop*80+32,16)))return false;
            Vector3 n=Vector(data,keep*80+12),d=Vector(data,drop*80+12);float lengths=n.LengthSquared()*d.LengthSquared();if(lengths>1e-20f&&Vector3.Dot(n,d)<.9f*MathF.Sqrt(lengths))return false;
            float u=Float(data,keep*80+24)-Float(data,drop*80+24),v=Float(data,keep*80+28)-Float(data,drop*80+28);return u*u+v*v<=MaximumUvDelta*MaximumUvDelta;
        }
        bool CanCollapse(int keep,int drop,out Dictionary<int,int> replacements){
            replacements=new();if(locked[drop])return false;
            var common=Neighbors(keep);common.IntersectWith(Neighbors(drop));var opposite=new HashSet<int>();int shared=0;var targets=new Dictionary<int,SortedSet<int>>();var variants=new HashSet<int>();
            foreach(int f in incident[drop])if(faces[f].Live){
                Face face=faces[f];int[] corners=face.Vertices();foreach(int v in corners)if(weld[v]==drop)variants.Add(v);
                int[] ids=Geo(face);if(!ids.Contains(keep))continue;shared++;foreach(int v in ids)if(v!=keep&&v!=drop)opposite.Add(v);
                int from=corners.First(v=>weld[v]==drop),to=corners.First(v=>weld[v]==keep);if(!targets.TryGetValue(from,out var list))targets[from]=list=new();list.Add(to);
            }
            if(shared!=2||!common.SetEquals(opposite))return false;
            foreach(int from in variants){
                if(!targets.TryGetValue(from,out var list))return false;int selected=-1;
                foreach(int to in list){
                    bool valid=true;foreach(int original in groups[from])if(!Attributes(to,original)||Vector3.DistanceSquared(positions[original],positions[to])>maximumBindError*maximumBindError||SkinDelta(original,to)>MaximumWeightDelta){valid=false;break;}
                    if(valid){selected=to;break;}
                }
                if(selected<0)return false;replacements.Add(from,selected);
            }
            // A two-face UV island can lose its last attribute representative
            // while its geometric vertex survives in another chart. Keep all
            // original source vertices in the geometric error test, including
            // those no longer referenced by a surviving chart.
            var futureKeep=new HashSet<int>();
            foreach(int f in incident[keep])if(faces[f].Live&&!Geo(faces[f]).Contains(drop))foreach(int v in faces[f].Vertices())if(weld[v]==keep)futureKeep.Add(v);
            foreach(int f in incident[drop])if(faces[f].Live&&!Geo(faces[f]).Contains(keep))foreach(int v in faces[f].Vertices())if(weld[v]==drop)futureKeep.Add(replacements[v]);
            foreach(int original in geometricOriginals[keep].Concat(geometricOriginals[drop])){
                bool represented=false;foreach(int to in futureKeep)if(Vector3.DistanceSquared(positions[original],positions[to])<=maximumBindError*maximumBindError&&SkinDelta(original,to)<=MaximumWeightDelta){represented=true;break;}
                if(!represented)return false;
            }
            var keys=new HashSet<(int,int,int)>();
            (int,int,int) Key(int a,int b,int c){int[] s={a,b,c};Array.Sort(s);return(s[0],s[1],s[2]);}
            foreach(int f in incident[keep])if(faces[f].Live&&!Geo(faces[f]).Contains(drop)){int[] ids=Geo(faces[f]);keys.Add(Key(ids[0],ids[1],ids[2]));}
            foreach(int f in incident[drop]){
                Face face=faces[f];if(!face.Live||Geo(face).Contains(keep))continue;
                int a=weld[face.A]==drop?replacements[face.A]:face.A,b=weld[face.B]==drop?replacements[face.B]:face.B,c=weld[face.C]==drop?replacements[face.C]:face.C;Vector3 n=Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]);
                float area=n.LengthSquared(),old=face.Normal.LengthSquared();if(area<1e-20f||Vector3.Dot(n,face.Normal)<=.5f*MathF.Sqrt(area*old)||!keys.Add(Key(weld[a],weld[b],weld[c])))return false;
            }
            return true;
        }
        int remaining=faces.Length,collapsed=0,target=Math.Max(4,(int)(faces.Length*.6f));
        while(remaining>target&&queue.TryDequeue(out Edge edge,out _)){
            if(!live[edge.A]||!live[edge.B]||Vector3.DistanceSquared(positions[edge.A],positions[edge.B])>maximumBindError*maximumBindError)continue;
            int keep=edge.A,drop=edge.B;if(!CanCollapse(keep,drop,out var replacements)){keep=edge.B;drop=edge.A;if(!CanCollapse(keep,drop,out replacements))continue;}
            int[] changed=incident[drop].ToArray();
            foreach(int f in changed){
                Face face=faces[f];if(!face.Live)continue;int[] geometry=Geo(face);foreach(int vertex in geometry)incident[vertex].Remove(f);
                if(geometry.Contains(keep)){face.Live=false;remaining--;continue;}
                if(weld[face.A]==drop)face.A=replacements[face.A];if(weld[face.B]==drop)face.B=replacements[face.B];if(weld[face.C]==drop)face.C=replacements[face.C];foreach(int vertex in Geo(face))incident[vertex].Add(f);
            }
            foreach(var pair in replacements){parents[pair.Key]=pair.Value;groups[pair.Value].AddRange(groups[pair.Key]);groups[pair.Key].Clear();}
            geometricParents[drop]=keep;geometricOriginals[keep].AddRange(geometricOriginals[drop]);geometricOriginals[drop].Clear();
            live[drop]=false;collapsed++;foreach(int neighbor in Neighbors(keep))Enqueue(Edge.Of(keep,neighbor));
        }
        var used=new SortedSet<int>();foreach(Face face in faces)if(face.Live)foreach(int vertex in face.Vertices())used.Add(vertex);
        var compact=Enumerable.Repeat(-1,nv).ToArray();int next=0;foreach(int vertex in used)compact[vertex]=next++;
        int Root(int vertex){while(parents[vertex]!=vertex)vertex=parents[vertex];return vertex;}
        int GeometricRoot(int vertex){while(geometricParents[vertex]!=vertex)vertex=geometricParents[vertex];return vertex;}
        correspondence=new int[nv];float maximum=0,weights=0;
        for(int i=0;i<nv;i++){
            int root=Root(i);
            if(compact[root]<0&&referenced[i]){
                int geometry=GeometricRoot(weld[i]);root=-1;
                foreach(int targetVertex in used)if(weld[targetVertex]==geometry&&Vector3.DistanceSquared(positions[i],positions[targetVertex])<=maximumBindError*maximumBindError&&SkinDelta(i,targetVertex)<=MaximumWeightDelta){root=targetVertex;break;}
                if(root<0)throw new InvalidDataException("LOD lost correspondence for a referenced source vertex");
            }
            correspondence[i]=root<0?-1:compact[root];if(correspondence[i]>=0){maximum=Math.Max(maximum,Vector3.Distance(positions[i],positions[root]));weights=Math.Max(weights,SkinDelta(i,root));}
        }
        if(maximum>maximumBindError+1e-7f||weights>MaximumWeightDelta+1e-7f)throw new InvalidDataException("LOD collapse exceeded error budget");
        byte[] header=(byte[])mesh.Header.Clone();Put(header,12,used.Count);Put(header,16,remaining*3);
        using var result=new MemoryStream();result.Write(header);foreach(int vertex in used)result.Write(data.AsSpan(vertex*80,80));foreach(Face face in faces)if(face.Live){Write(result,compact[face.A]);Write(result,compact[face.B]);Write(result,compact[face.C]);}
        cooked=result.ToArray();return new(nv,used.Count,faces.Length,remaining,maximum,weights,collapsed);
    }
}
