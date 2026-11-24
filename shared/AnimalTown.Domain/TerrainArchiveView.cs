namespace AnimalTown;

// Select immutable sections without stream jobs, lookups or repair.
// The half-metre guard is the same conservative camera/height envelope used by
// the streaming oracle. Geometry stays in logical world coordinates.
public sealed class TerrainArchiveView
{
    public readonly int[] Sections=new int[TerrainStream.Capacity];
    public int Count {get;private set;}
    public int Revision {get;private set;}
    private readonly int[] marks=new int[TerrainArchivePlan.Count];
    private int stamp;
    private float cachedX=float.NaN,cachedZ,cachedGuard,cachedAspect;
    public void Reset(){cachedX=float.NaN;Count=0;Revision++;}
    public void Prepare(float x,float z,CylinderRows rows,float guard=0)
    {
        if(!(guard>=0)||guard>TerrainStream.Prefetch)throw new ArgumentOutOfRangeException(nameof(guard));
        float fx=(float)Math.Floor(x*2)*.5f+.25f,fz=(float)Math.Floor(z*2)*.5f+.25f;
        if(cachedX==fx&&cachedZ==fz&&cachedGuard==guard&&cachedAspect==RenderView.AspectRatio)return;
        cachedX=fx;cachedZ=fz;cachedGuard=guard;cachedAspect=RenderView.AspectRatio;Count=0;Revision++;
        if(++stamp==int.MaxValue){Array.Clear(marks,0,marks.Length);stamp=1;}
        Append(fx,fz,rows,guard);
    }
    private void Append(float fx,float fz,CylinderRows rows,float guard)
    {
        // Expand the complete envelope, including intermediate camera positions
        // where the curved horizon can expose a page between sampled views.
        var motion=new CylinderMotion(fz);
        int top=(int)Math.Floor((fz-TerrainTiles.GroundRowsAhead-TerrainStream.Prefetch)/TerrainStream.Height)*TerrainStream.Height;
        int bottom=(int)Math.Floor((fz+TerrainTiles.GroundRowsBehind+TerrainStream.Prefetch)/TerrainStream.Height)*TerrainStream.Height;
        int left=(int)Math.Floor((fx-CylinderWorld.ViewHalfWidth-TerrainStream.Prefetch)/TerrainStream.Width)*TerrainStream.Width;
        int right=(int)Math.Floor((fx+CylinderWorld.ViewHalfWidth+TerrainStream.Prefetch)/TerrainStream.Width)*TerrainStream.Width;
        for(int cz=top;cz<=bottom;cz+=TerrainStream.Height){
            var center=rows.At(cz+TerrainStream.Height/2,1);motion.Point(center.Y,center.Z,out float py,out float pz);
            if(!RenderView.TerrainHorizon(py,pz,3+guard*2,fz))continue;
            var bounds=new TerrainSectionBounds(rows,motion,cz,fz);
            float width=RenderView.BoxRowWidth(bounds.MinY-guard,bounds.MinZ-guard*2,bounds.MaxY+guard,bounds.MaxZ+guard*2,TerrainStream.Width*.5f+TerrainSectionBounds.SideGuard+guard,fx,fz);
            if(width<0)continue;
            for(int cx=left;cx<=right;cx+=TerrainStream.Width){
                if(Math.Abs(cx+TerrainStream.Width*.5f-fx)>width)continue;
                int pageZ=(int)Math.Floor(cz/(float)TerrainArchivePlan.Height)*TerrainArchivePlan.Height;
                int section=TerrainArchivePlan.Index(cx,pageZ);if(marks[section]==stamp)continue;marks[section]=stamp;
                if(Count==Sections.Length)throw new InvalidOperationException("Terrain view exceeds its fixed section budget");
                Sections[Count++]=section;
            }
        }
    }
}
