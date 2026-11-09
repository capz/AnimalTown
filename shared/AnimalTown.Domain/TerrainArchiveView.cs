namespace AnimalTown;

// Select immutable sections without stream jobs, lookups, prefetch or repair.
// The half-metre guard is the same conservative camera/height envelope used by
// the streaming oracle. Geometry stays in logical world coordinates.
public sealed class TerrainArchiveView
{
    public readonly int[] Sections=new int[TerrainStream.Capacity];
    public int Count {get;private set;}
    public int Revision {get;private set;}
    private float cachedX=float.NaN,cachedZ;
    public void Reset(){cachedX=float.NaN;Count=0;Revision++;}
    public void Prepare(float x,float z,CylinderRows rows)
    {
        float fx=(float)Math.Floor(x*2)*.5f+.25f,fz=(float)Math.Floor(z*2)*.5f+.25f;
        if(cachedX==fx&&cachedZ==fz)return;
        cachedX=fx;cachedZ=fz;Count=0;Revision++;
        var motion=new CylinderMotion(fz);
        int top=(int)Math.Floor((fz-TerrainTiles.GroundRowsAhead-TerrainStream.Prefetch)/TerrainStream.Height)*TerrainStream.Height;
        int bottom=(int)Math.Floor((fz+TerrainTiles.GroundRowsBehind+TerrainStream.Prefetch)/TerrainStream.Height)*TerrainStream.Height;
        int left=(int)Math.Floor((fx-CylinderWorld.ViewHalfWidth-TerrainStream.Prefetch)/TerrainStream.Width)*TerrainStream.Width;
        int right=(int)Math.Floor((fx+CylinderWorld.ViewHalfWidth+TerrainStream.Prefetch)/TerrainStream.Width)*TerrainStream.Width;
        for(int cz=top;cz<=bottom;cz+=TerrainStream.Height){
            var center=rows.At(cz+TerrainStream.Height/2,1);motion.Point(center.Y,center.Z,out float py,out float pz);
            if(!RenderView.TerrainHorizon(py,pz,3,fz))continue;
            var bounds=new TerrainSectionBounds(rows,motion,cz,fz);
            float width=RenderView.BoxRowWidth(bounds.MinY,bounds.MinZ,bounds.MaxY,bounds.MaxZ,TerrainStream.Width*.5f+TerrainSectionBounds.SideGuard,fx,fz);
            if(width<0)continue;
            for(int cx=left;cx<=right;cx+=TerrainStream.Width){
                if(Math.Abs(cx+TerrainStream.Width*.5f-fx)>width)continue;
                if(Count==Sections.Length)throw new InvalidOperationException("Terrain view exceeds its fixed section budget");
                Sections[Count++]=TerrainArchivePlan.Index(cx,cz);
            }
        }
    }
}
