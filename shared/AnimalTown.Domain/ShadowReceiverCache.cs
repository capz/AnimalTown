namespace AnimalTown;

// Bounded warm scenery receivers. Exact local terrain snapshots allow a distant
// terrain edit to keep valid receivers; no save data or native mesh is retained.
public sealed class ShadowReceiverCache
{
    public const int Capacity=64,MaximumCells=81;
    public const int MaximumPayloadBytes=Capacity*(GroundShadow.MaximumTriangles*6*4+MaximumCells*2);
    private sealed class Entry
    {
        internal bool Valid,Flat;
        internal int Count,Version,Left,Top,Width,Height;
        internal long Use;
        internal ShadowCaster Caster;
        internal readonly float[] Uvs=new float[GroundShadow.MaximumTriangles*6];
        internal readonly byte[] Terrain=new byte[MaximumCells*2];
    }
    private readonly Entry[] entries=new Entry[Capacity];
    private Town? town;
    private long clock;
    public ShadowReceiverCache(){for(int i=0;i<entries.Length;i++)entries[i]=new Entry();}
    private static bool Same(ShadowCaster a,ShadowCaster b)=>a.X==b.X&&a.Z==b.Z&&a.Scale==b.Scale&&a.Profile==b.Profile&&a.Height==b.Height&&a.Left==b.Left&&a.Right==b.Right&&a.Top==b.Top&&a.Bottom==b.Bottom;
    private void Source(Town source){if(town==source)return;town=source;for(int i=0;i<entries.Length;i++)entries[i].Valid=false;}
    private static bool TerrainMatches(Town source,Entry e,bool write)
    {
        int at=0;
        for(int z=e.Top;z<e.Top+e.Height;z++)for(int x=e.Left;x<e.Left+e.Width;x++){
            byte tile=(byte)source.At(x,z),mode=(byte)TerrainElevation.Mode(source,x,z);
            if(write){e.Terrain[at++]=tile;e.Terrain[at++]=mode;}
            else if(e.Terrain[at++]!=tile||e.Terrain[at++]!=mode)return false;
        }
        return true;
    }
    private Entry? Find(Town source,ShadowCaster caster)
    {
        Source(source);
        for(int i=0;i<entries.Length;i++){
            var e=entries[i];if(!e.Valid||!Same(e.Caster,caster))continue;
            if(e.Version!=source.TerrainVersion){if(!TerrainMatches(source,e,false)){e.Valid=false;return null;}e.Version=source.TerrainVersion;}
            e.Use=++clock;return e;
        }
        return null;
    }
    public bool Valid(Town source,ShadowCaster caster)=>Find(source,caster)!=null;
    public bool Restore(Town source,ShadowCaster caster,float[] destination,out int count,out bool flat)
    {
        var e=Find(source,caster);count=e?.Count??0;flat=e?.Flat??false;if(e==null)return false;
        Array.Copy(e.Uvs,destination,count*2);return true;
    }
    public void Store(Town source,ShadowCaster caster,float[] uv,int count,bool flat)
    {
        Source(source);
        int left=(int)Math.Floor(Math.Min(caster.Left,caster.X))-1,top=(int)Math.Floor(Math.Min(caster.Top,caster.Z))-1;
        int width=(int)Math.Floor(Math.Max(caster.Right,caster.X))+2-left,height=(int)Math.Floor(Math.Max(caster.Bottom,caster.Z))+2-top;
        if(count<0||count>GroundShadow.MaximumTriangles*3||width*height>MaximumCells)return;
        int oldest=0;for(int i=0;i<entries.Length;i++){if(!entries[i].Valid){oldest=i;break;}if(entries[i].Use<entries[oldest].Use)oldest=i;}
        var e=entries[oldest];e.Valid=true;e.Caster=caster;e.Count=count;e.Flat=flat;e.Version=source.TerrainVersion;e.Use=++clock;
        e.Left=left;e.Top=top;e.Width=width;e.Height=height;Array.Copy(uv,e.Uvs,count*2);TerrainMatches(source,e,true);
    }
}
