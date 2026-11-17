using AnimalTown;

internal static class PathChecks
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        public byte[] Read(string name)=>Files[name];
        public void Write(string name,byte[] bytes)=>Files[name]=(byte[])bytes.Clone();
    }
    internal static void Run(Action<bool,string> check)
    {
        int min=int.MaxValue,max=0,unconnected=0;long elapsed=0,allocated=0;
        for(int seed=0;seed<24;seed++){
            var t=new Town(seed);var terrain=(byte[])t.Tiles.Clone();var trees=t.Ground.Take(t.GroundCount).Select(g=>(g.X,g.Z,g.Kind)).ToArray();
            var expected=(byte[])t.Paths.Packed.Clone();long before=GC.GetAllocatedBytesForCurrentThread();var watch=System.Diagnostics.Stopwatch.StartNew();TownPathPlanner.Generate(t);watch.Stop();elapsed+=watch.ElapsedTicks;allocated+=GC.GetAllocatedBytesForCurrentThread()-before;
            check(expected.SequenceEqual(t.Paths.Packed)&&terrain.SequenceEqual(t.Tiles),"path routing is deterministic and never changes terrain");
            check(t.Paths.Packed.Length==3840&&t.Paths.Count>100,"new towns use the bounded two-bit overlay and get a road network");
            min=Math.Min(min,t.Paths.Count);max=Math.Max(max,t.Paths.Count);unconnected+=t.Paths.Unconnected;
            for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)if(t.Paths.At(x,z)!=0){
                check(TownPaths.Surface(t,x,z)&&t.Walkable(x,z)&&!t.InReserve(x,z),"roads preserve water, buildings, beaches and the starter garden");
                check(!trees.Any(g=>(g.Kind==2||g.Kind==3)&&Math.Abs(g.X-x)<=1&&Math.Abs(g.Z-z)<=1),"the full road width clears existing trees and planted items");
                bool wide=false;for(int dz=-1;dz<=0;dz++)for(int dx=-1;dx<=0;dx++)if(t.Paths.At(x+dx,z+dz)!=0&&t.Paths.At(x+dx+1,z+dz)!=0&&t.Paths.At(x+dx,z+dz+1)!=0&&t.Paths.At(x+dx+1,z+dz+1)!=0)wide=true;
                // Plaza/bridge cells retain their own material within the lane.
                if(!wide)wide=t.At(x-1,z)==(int)Terrain.Plaza||t.At(x+1,z)==(int)Terrain.Plaza||t.At(x,z-1)==(int)Terrain.Plaza||t.At(x,z+1)==(int)Terrain.Plaza||t.At(x-1,z)==(int)Terrain.Bridge||t.At(x+1,z)==(int)Terrain.Bridge||t.At(x,z-1)==(int)Terrain.Bridge||t.At(x,z+1)==(int)Terrain.Bridge;
                check(wide,"main paths have a full two-metre footprint including turns");
            }
        }
        var px=new float[8];var pz=new float[8];
        for(int mask=0;mask<16;mask++){
            int n=PathShape.Polygon(mask,px,pz);double area=0;
            for(int i=0;i<n;i++){int next=(i+1)%n;area+=px[i]*pz[next]-pz[i]*px[next];check(px[i]>=0&&px[i]<=1&&pz[i]>=0&&pz[i]<=1,"path polygon remains inside its cell");}
            int cut=System.Numerics.BitOperations.PopCount((uint)mask);
            check(Math.Abs(area*.5+cut*PathShape.Bevel*PathShape.Bevel*.5-1)<.000001,"road and grass corner triangles partition the ground without gaps or overlap");
        }
        var s=new GameState(3);s.Tutorial=6;s.Rollover(21014,2027*12+7);var bytes=SaveCodec.Encode(s);
        check(BitConverter.ToInt32(bytes,4)==10&&s.Town.Paths.Packed.SequenceEqual(SaveCodec.Decode(bytes).Town.Paths.Packed),"whole-town v10 preserves roads alongside shop state");
        var parts=SplitSaveCodec.Encode(s);check(SplitSaveCodec.ReadVersion(parts[0])==6&&s.Town.Paths.Packed.SequenceEqual(SplitSaveCodec.Decode(parts).Town.Paths.Packed),"split town v6 preserves roads");
        var storage=new Storage();var repository=new SaveRepository(storage);repository.Save(s);
        s.Town.Paths.Set(32,40,s.Town.Paths.At(32,40)==0?1:0);s.Town.NotifyTerrainChanged();repository.Save(s);
        check(s.Town.Paths.Packed.SequenceEqual(new SaveRepository(storage).Load()!.Town.Paths.Packed),"incremental publication includes edited path content");
        // An actual old schema, not current bytes relabelled with an old version.
        var old=parts[0][..^(TownPaths.PackedBytes+8)];Put(old,4,4);Put(old,24,old.Length);Crc(old);parts[0]=old;
        check(SplitSaveCodec.Decode(parts).Town.Paths.Count==0,"v4 shared towns load an empty overlay without replanning");
        var pathArchive=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures/town-paths-v9.bin"));
        old=pathArchive[..^(TownPaths.PackedBytes+8)];Put(old,4,8);Put(old,16,old.Length);Put(old,20,(int)SaveCodec.Crc(old,24,old.Length-24));
        check(SaveCodec.Decode(old).Town.Paths.Count==0,"v8 combined towns load an empty overlay without replanning");
        var broken=SplitSaveCodec.Encode(s);broken[0][^5]=255;Crc(broken[0]);bool rejected=false;try{SplitSaveCodec.Decode(broken);}catch(InvalidOperationException){rejected=true;}check(rejected,"unsupported style bits are rejected even with a valid checksum");
        Console.WriteLine($"Paths: 24 seeds, {min}..{max} painted cells, {unconnected} explicitly unconnected anchors; planner mean {elapsed*1000.0/System.Diagnostics.Stopwatch.Frequency/24:F2}ms host / {allocated/24} temporary managed bytes; 3840 retained bytes");
    }
    private static void Put(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(8*i));}
    private static void Crc(byte[] b)=>Put(b,b.Length-4,(int)SaveCodec.Crc(b,0,b.Length-4));
}
