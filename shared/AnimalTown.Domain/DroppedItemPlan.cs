namespace AnimalTown;

// Eye-facing outdoor sprites, bounded by the saved town's ground capacity.
// The snapshot avoids uploads while standing still; the sort reuses scratch space.
public sealed class DroppedItemPlan
{
    public const int Capacity=512;
    public const float Size=.65f,GroundOffset=.03f;
    public readonly float[] X=new float[Capacity],Y=new float[Capacity],Z=new float[Capacity],Depth=new float[Capacity];
    public readonly int[] Order=new int[Capacity];
    private readonly int[] scratch=new int[Capacity],snapshot=new int[Capacity];
    private Town? previousTown;
    private int previousCount=-1,terrainVersion;
    private float previousX,previousZ,previousHeight;
    public int Count {get;private set;}
    public long Revision {get;private set;}
    public float EyeX {get;private set;}
    public float EyeY {get;private set;}
    public float EyeZ {get;private set;}
    public DroppedItemFacing Facing(int index)=>new DroppedItemFacing(EyeX-X[index],EyeY-Y[index],EyeZ-Z[index]);
    public bool Prepare(Town town,float focusX,float focusZ,float eyeHeight)
    {
        bool changed=previousTown!=town||previousCount!=town.GroundCount||terrainVersion!=town.TerrainVersion||previousX!=focusX||previousZ!=focusZ||previousHeight!=eyeHeight;
        for(int i=0;i<town.GroundCount;i++){
            var g=town.Ground[i];int value=g.Kind==0?1+g.X+g.Z*Town.Width:0;
            if(snapshot[i]!=value){snapshot[i]=value;changed=true;}
        }
        if(!changed)return false;
        previousTown=town;previousCount=town.GroundCount;terrainVersion=town.TerrainVersion;previousX=focusX;previousZ=focusZ;previousHeight=eyeHeight;
        EyeX=focusX;EyeY=CylinderWorld.CameraHeight+eyeHeight;EyeZ=focusZ+CylinderWorld.CameraBack;
        Count=0;
        for(int i=0;i<town.GroundCount;i++){
            var g=town.Ground[i];if(g.Kind!=0)continue;
            float x=g.X+.5f;var p=CylinderWorld.Surface(g.Z+.5f,focusZ,TerrainElevation.At(town,x,g.Z+.5f));
            float y=p.Y+GroundOffset;var facing=new DroppedItemFacing(EyeX-x,EyeY-y,EyeZ-p.Z);
            float cx=x+facing.UpX*Size*.5f,cy=y+facing.UpY*Size*.5f,cz=p.Z+facing.UpZ*Size*.5f;
            const float radius=Size*.71f;
            if(!RenderView.Sphere(cx,cy,cz,radius,focusX,focusZ,eyeHeight)||!RenderView.AboveHorizon(cy,cz,radius,focusZ,eyeHeight))continue;
            X[Count]=x;Y[Count]=y;Z[Count]=p.Z;
            Depth[Count]=(cy-(CylinderWorld.CameraHeight+eyeHeight))*RenderView.ForwardY+(cz-focusZ-CylinderWorld.CameraBack)*RenderView.ForwardZ;
            Order[Count]=Count;Count++;
        }
        // Stable far-to-near merge sort, O(n log n), without per-frame garbage.
        for(int width=1;width<Count;width*=2){
            for(int left=0;left<Count;left+=width*2){
                int mid=Math.Min(left+width,Count),end=Math.Min(left+width*2,Count),a=left,b=mid,write=left;
                while(a<mid||b<end)scratch[write++]=b>=end||(a<mid&&Depth[Order[a]]>=Depth[Order[b]])?Order[a++]:Order[b++];
            }
            Array.Copy(scratch,Order,Count);
        }
        Revision++;return true;
    }
}

// Spherical billboard basis: yaw AND pitch follow the eye-to-drop ray, while
// the horizontal bottom edge stays level and anchored above the curved ground.
public readonly struct DroppedItemFacing
{
    public readonly float RightX,RightZ,UpX,UpY,UpZ;
    public DroppedItemFacing(float x,float y,float z)
    {
        float length=(float)Math.Sqrt(x*x+y*y+z*z);
        if(length<.00001f){x=0;y=-RenderView.ForwardY;z=-RenderView.ForwardZ;length=1;}
        x/=length;y/=length;z/=length;
        float horizontal=(float)Math.Sqrt(x*x+z*z);
        if(horizontal<.00001f){RightX=1;RightZ=0;UpX=0;UpY=0;UpZ=y>=0?-1:1;return;}
        RightX=z/horizontal;RightZ=-x/horizontal;
        UpX=y*RightZ;UpY=z*RightX-x*RightZ;UpZ=-y*RightX;
    }
}
