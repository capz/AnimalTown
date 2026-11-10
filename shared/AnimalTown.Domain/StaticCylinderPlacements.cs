namespace AnimalTown;

public readonly struct StaticCylinderPlacement
{
    public readonly CylinderPose Surface;
    public readonly float HalfSin,HalfCos;
    public StaticCylinderPlacement(CylinderPose surface)
    {Surface=surface;HalfSin=(float)Math.Sin(surface.Pitch*.5f);HalfCos=(float)Math.Cos(surface.Pitch*.5f);}
}
// Logical positions stay unchanged. Only the common scroll changes each frame.
public sealed class StaticCylinderPlacements
{
    private readonly StaticCylinderPlacement[] poses;
    private readonly int[] xs,zs,revisions;
    private readonly float[] heights;
    private readonly bool[] valid;
    private object? owner;
    private int revision;
    private float focus=float.NaN;
    public const float ReferenceZ=Town.Height*.5f;
    public CylinderScroll Scroll {get;private set;}
    public int Builds {get;private set;}
    public StaticCylinderPlacements(int capacity)
    {if(capacity<1||capacity>512)throw new ArgumentOutOfRangeException();poses=new StaticCylinderPlacement[capacity];xs=new int[capacity];zs=new int[capacity];revisions=new int[capacity];heights=new float[capacity];valid=new bool[capacity];}
    public void Begin(object nextOwner,int nextRevision,float focusZ)
    {
        if(!ReferenceEquals(owner,nextOwner)){owner=nextOwner;Array.Clear(valid,0,valid.Length);focus=float.NaN;}
        revision=nextRevision;
        if(focus!=focusZ){focus=focusZ;Scroll=CylinderWorld.Scroll(ReferenceZ,focusZ);}
    }
    public StaticCylinderPlacement Get(int slot,int x,int z,float height)
    {
        if(slot<0||slot>=poses.Length)throw new ArgumentOutOfRangeException();
        if(!valid[slot]||xs[slot]!=x||zs[slot]!=z||heights[slot]!=height||revisions[slot]!=revision){
            poses[slot]=new StaticCylinderPlacement(CylinderWorld.Surface(z,ReferenceZ,height));xs[slot]=x;zs[slot]=z;heights[slot]=height;revisions[slot]=revision;valid[slot]=true;Builds++;
        }
        return poses[slot];
    }
    public CylinderPose Project(StaticCylinderPlacement placement,float extraHeight=0)
    {
        var p=placement.Surface;var s=Scroll;float y=p.Y+p.UpY*extraHeight,z=p.Z+p.UpZ*extraHeight;
        return new CylinderPose(s.Cos*y-s.Sin*z+s.Y,s.Sin*y+s.Cos*z+s.Z,s.Cos*p.UpY-s.Sin*p.UpZ,s.Sin*p.UpY+s.Cos*p.UpZ,0);
    }
}
