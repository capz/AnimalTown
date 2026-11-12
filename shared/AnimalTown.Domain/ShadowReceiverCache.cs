namespace AnimalTown;

// Bounded warm scenery receivers. Exact local terrain snapshots allow a distant
// terrain edit to keep valid receivers; no save data or native mesh is retained.
public sealed class ShadowReceiverCache
{
    public const int Capacity=64,MaximumCells=81;
    private const int MaximumCoordinates=GroundShadow.MaximumTriangles*6;
    public const int MaximumPayloadBytes=Capacity*(MaximumCoordinates*4+MaximumCells*2);
    private sealed class Entry
    {
        internal bool Valid,Flat;
        internal int Count,Offset,Version,Left,Top,Width,Height;
        internal long Use;
        internal ShadowCaster Caster;
    }
    private readonly Entry[] entries=new Entry[Capacity];
    private readonly byte[] terrain=new byte[Capacity*MaximumCells*2];
    // All 64 flat receivers fit immediately. Complex receivers share a bounded
    // arena which grows only when a cold workload exceeds its prior high water.
    private float[] coordinates=new float[Capacity*12];
    private int used;
    public int ResidentPayloadBytes=>terrain.Length+coordinates.Length*4;
    private Town? town;
    private long clock;
    public ShadowReceiverCache(){for(int i=0;i<entries.Length;i++)entries[i]=new Entry();}
    private static bool Same(ShadowCaster a,ShadowCaster b)=>a.X==b.X&&a.Z==b.Z&&a.Scale==b.Scale&&a.Profile==b.Profile&&a.Height==b.Height&&a.Left==b.Left&&a.Right==b.Right&&a.Top==b.Top&&a.Bottom==b.Bottom;
    private void Source(Town source){if(town==source)return;town=source;for(int i=0;i<entries.Length;i++)entries[i].Valid=false;used=0;}
    private bool TerrainMatches(Town source,int index,bool write)
    {
        var e=entries[index];int at=index*MaximumCells*2;
        for(int z=e.Top;z<e.Top+e.Height;z++)for(int x=e.Left;x<e.Left+e.Width;x++){
            byte tile=(byte)source.At(x,z),mode=(byte)TerrainElevation.Mode(source,x,z);
            if(write){terrain[at++]=tile;terrain[at++]=mode;}
            else if(terrain[at++]!=tile||terrain[at++]!=mode)return false;
        }
        return true;
    }
    private Entry? Find(Town source,ShadowCaster caster)
    {
        Source(source);
        for(int i=0;i<entries.Length;i++){
            var e=entries[i];if(!e.Valid||!Same(e.Caster,caster))continue;
            if(e.Version!=source.TerrainVersion){if(!TerrainMatches(source,i,false)){e.Valid=false;return null;}e.Version=source.TerrainVersion;}
            e.Use=++clock;return e;
        }
        return null;
    }
    public bool Valid(Town source,ShadowCaster caster)=>Find(source,caster)!=null;
    public bool Restore(Town source,ShadowCaster caster,float[] destination,out int count,out bool flat)
    {
        var e=Find(source,caster);count=e?.Count??0;flat=e?.Flat??false;if(e==null)return false;
        Array.Copy(coordinates,e.Offset,destination,0,count*2);return true;
    }
    // Move live spans down in their existing address order. No temporary list,
    // no changed recency, and Array.Copy handles overlapping source/destination.
    private void Compact()
    {
        int read=0,write=0;
        while(true){
            Entry? next=null;
            for(int i=0;i<entries.Length;i++){
                var e=entries[i];if(!e.Valid||e.Count==0||e.Offset<read)continue;
                if(next==null||e.Offset<next.Offset)next=e;
            }
            if(next==null)break;
            int length=next.Count*2;read=next.Offset+length;
            if(next.Offset!=write)Array.Copy(coordinates,next.Offset,coordinates,write,length);
            next.Offset=write;write+=length;
        }
        used=write;
    }
    public void Store(Town source,ShadowCaster caster,float[] uv,int count,bool flat)
    {
        Source(source);
        int left=(int)Math.Floor(Math.Min(caster.Left,caster.X))-1,top=(int)Math.Floor(Math.Min(caster.Top,caster.Z))-1;
        int width=(int)Math.Floor(Math.Max(caster.Right,caster.X))+2-left,height=(int)Math.Floor(Math.Max(caster.Bottom,caster.Z))+2-top;
        if(count<0||count>GroundShadow.MaximumTriangles*3||width*height>MaximumCells)return;
        int length=count*2;if(uv.Length<length)throw new ArgumentException("Incomplete receiver UVs",nameof(uv));
        int oldest=0;for(int i=0;i<entries.Length;i++){if(!entries[i].Valid){oldest=i;break;}if(entries[i].Use<entries[oldest].Use)oldest=i;}
        var e=entries[oldest];int offset=e.Offset;
        bool reuse=e.Valid&&length<=e.Count*2;e.Valid=false;
        if(!reuse){
            if(used+length>coordinates.Length)Compact();
            if(used+length>coordinates.Length){
                int size=Math.Min(Capacity*MaximumCoordinates,Math.Max(coordinates.Length*2,used+length));
                var replacement=new float[size];Array.Copy(coordinates,replacement,used);coordinates=replacement;
            }
            offset=used;used+=length;
        }
        Array.Copy(uv,0,coordinates,offset,length);
        e.Caster=caster;e.Count=count;e.Offset=offset;e.Flat=flat;e.Version=source.TerrainVersion;e.Use=++clock;
        e.Left=left;e.Top=top;e.Width=width;e.Height=height;TerrainMatches(source,oldest,true);e.Valid=true;
    }
}
