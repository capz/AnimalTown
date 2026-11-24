namespace AnimalTown;

// Bounded projected opaque proxies. A whole enclosing rectangle must fit
// inside ONE opaque polygon and lie behind its furthest point. Partial cover,
// holes, ramps, unknown art and bounds crossing the eye all keep the actor.
public sealed class OpaqueActorOcclusion
{
    private const int Capacity=160;
    private readonly float[] polygons=new float[Capacity*8],depths=new float[Capacity];
    private readonly int[] terrainX0=new int[128],terrainX1=new int[128],terrainZ=new int[128],terrainEndZ=new int[128];
    private static readonly CylinderRows referenceRows=new CylinderRows();
    private CylinderMotion motion;
    private Town? projectedTown;
    private float projectedX=float.NaN,projectedZ,projectedEye;
    private int projectedVersion,projectedTypes;
    private bool projectedTerrain;
    private readonly float[] buildingX=new float[BuildingWorld.Count],buildingZ=new float[BuildingWorld.Count];
    private readonly int[] buildingType=new int[BuildingWorld.Count];
    private readonly bool[] buildingExists=new bool[BuildingWorld.Count];
    private readonly BuildingDefinition?[] buildingDefinition=new BuildingDefinition?[BuildingWorld.Count];
    private Town? cachedTown;private int terrainCount,terrainVersion=-1,anchorX=int.MinValue,anchorZ;
    private int count;private float eyeX,eyeY,eyeZ;
    public int Count=>count;
    public int ProjectionBuilds {get;private set;}
    public void Prepare(GameState state,float eyeHeight,int buildingTypes,bool terrain)
    {
        if(projectedTown==state.Town&&projectedX==state.X&&projectedZ==state.Z&&projectedEye==eyeHeight&&
            projectedVersion==state.Town.TerrainVersion&&projectedTypes==buildingTypes&&projectedTerrain==terrain&&
            (buildingTypes==0||BuildingsMatch(state,false)))return;
        projectedTown=state.Town;projectedX=state.X;projectedZ=state.Z;projectedEye=eyeHeight;
        projectedVersion=state.Town.TerrainVersion;projectedTypes=buildingTypes;projectedTerrain=terrain;
        if(buildingTypes!=0)BuildingsMatch(state,true);
        ProjectionBuilds++;motion=new CylinderMotion(state.Z);
        count=0;eyeX=state.X;eyeY=eyeHeight+CylinderWorld.CameraHeight;eyeZ=state.Z+CylinderWorld.CameraBack;
        for(int building=0;building<BuildingWorld.Count;building++){
            if(!BuildingWorld.Exists(state,building)||(buildingTypes&(1<<state.BuildingTypes[building]))==0)continue;
            var definition=BuildingWorld.Definition(state,building);
            // These generated kits have an uninterrupted rear wall. Restrict
            // proxies to their names; future art must explicitly earn a proxy.
            if(definition.Exterior!="cottage-exterior"&&definition.Exterior!="shop-exterior")continue;
            BuildingWorld.Origin(state,building,out float x,out float z);
            var p=Surface((int)z,TerrainElevation.At(state.Town,x+1.5f,z+1.5f));
            // Strictly inside the wall common to both kits (3m x >=2.35m).
            const float back=.06f;
            float lowY=p.Y+.08f*p.UpY-back*p.UpZ,lowZ=p.Z+.08f*p.UpZ+back*p.UpY;
            float highY=p.Y+2.25f*p.UpY-back*p.UpZ,highZ=p.Z+2.25f*p.UpZ+back*p.UpY;
            Add(x+.08f,lowY,lowZ,x+2.92f,lowY,lowZ,x+2.92f,highY,highZ,x+.08f,highY,highZ);
        }
        if(!terrain)return;
        PrepareTerrain(state.Town,(int)CylinderWorld.ScrollAnchor(state.X),(int)CylinderWorld.ScrollAnchor(state.Z));
        for(int i=0;i<terrainCount&&count<Capacity;i++){
            AddTerrain(terrainX0[i]+.05f,terrainX1[i]-.05f,terrainZ[i],terrainEndZ[i]);
        }
    }
    private bool BuildingsMatch(GameState state,bool capture)
    {
        bool same=true;
        for(int i=0;i<BuildingWorld.Count;i++){
            bool exists=BuildingWorld.Exists(state,i);float x=0,z=0;
            if(exists)BuildingWorld.Origin(state,i,out x,out z);
            int type=state.BuildingTypes[i];var definition=exists?BuildingWorld.Definition(state,i):null;
            if(buildingExists[i]!=exists||buildingX[i]!=x||buildingZ[i]!=z||buildingType[i]!=type||buildingDefinition[i]!=definition)same=false;
            if(capture){buildingExists[i]=exists;buildingX[i]=x;buildingZ[i]=z;buildingType[i]=type;buildingDefinition[i]=definition;}
        }
        return same;
    }
    private CylinderPose Surface(int z,float height)
    {
        var p=referenceRows.At(z,height);motion.Point(p.Y,p.Z,out float y,out float worldZ);
        return new CylinderPose(y,worldZ,motion.Cos*p.UpY-motion.Sin*p.UpZ,motion.Sin*p.UpY+motion.Cos*p.UpZ,0);
    }
    private void PrepareTerrain(Town town,int x,int z)
    {
        if(cachedTown==town&&terrainVersion==town.TerrainVersion&&anchorX==x&&anchorZ==z)return;
        cachedTown=town;terrainVersion=town.TerrainVersion;anchorX=x;anchorZ=z;terrainCount=0;
        int left=Math.Max(1,x-32),right=Math.Min(Town.Width-1,x+32);
        for(int row=Math.Max(1,z-48);row<Math.Min(Town.Height-1,z+12)&&terrainCount<terrainZ.Length;row++){
            int start=-1;
            for(int col=left;col<=right;col++){
                bool solid=col<right&&Solid(town,col,row);
                if(solid&&start<0)start=col;
                if(!solid&&start>=0){StoreTerrain(start,col,row);start=-1;if(terrainCount==terrainZ.Length)break;}
            }
        }
    }
    private void StoreTerrain(int x0,int x1,int z)
    {
        for(int i=0;i<terrainCount;i++)if(terrainX0[i]==x0&&terrainX1[i]==x1&&terrainEndZ[i]==z){terrainEndZ[i]=z+1;return;}
        terrainX0[terrainCount]=x0;terrainX1[terrainCount]=x1;terrainZ[terrainCount]=z;terrainEndZ[terrainCount++]=z+1;
    }
    private void AddTerrain(float x0,float x1,int z0,int z1)
    {
        float left=float.NegativeInfinity,right=float.PositiveInfinity,bottom=float.PositiveInfinity,top=float.NegativeInfinity,far=0;int points=0;
        for(int z=z0;z<=z1;z++){
            var p=Surface(z,2);float y=p.Y,wz=p.Z;
            if(z==z0||z==z1){var next=Surface(z+(z==z0?1:-1),2);y=y*.98f+next.Y*.02f;wz=wz*.98f+next.Z*.02f;}
            // Only front-facing uninterrupted raised ground may occlude.
            if(p.UpY*(eyeY-y)+p.UpZ*(eyeZ-wz)<=.05f){if(points!=0)break;continue;}
            float d=Depth(y,wz);if(d<=CylinderWorld.Near){if(points!=0)break;continue;}
            left=Math.Max(left,(x0-eyeX)/d);right=Math.Min(right,(x1-eyeX)/d);
            float up=Up(y,wz)/d;bottom=Math.Min(bottom,up);top=Math.Max(top,up);far=Math.Max(far,d);
            points++;
        }
        if(points<2||left>=right||top-bottom<.00001f)return;
        // The connected sequence of exact rendered row chords covers every
        // vertical coordinate between its projected extrema. Intersecting all
        // horizontal spans creates an INSCRIBED rectangle, never the convex
        // hull of a curved silhouette that could cover empty space.
        int at=count*8;polygons[at]=left;polygons[at+1]=bottom;polygons[at+2]=right;polygons[at+3]=bottom;
        polygons[at+4]=right;polygons[at+5]=top;polygons[at+6]=left;polygons[at+7]=top;depths[count++]=far;
    }
    private static bool Solid(Town town,int x,int z)
    {
        for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
            if(TerrainElevation.Mode(town,x+dx,z+dz)!=TerrainElevation.Block||town.At(x+dx,z+dz)!=(int)Terrain.Grass)return false;
        return true;
    }
    private float Depth(float y,float z)=>(y-eyeY)*RenderView.ForwardY+(z-eyeZ)*RenderView.ForwardZ;
    private float Up(float y,float z)=>-(y-eyeY)*RenderView.ForwardZ+(z-eyeZ)*RenderView.ForwardY;
    private void Add(float ax,float ay,float az,float bx,float by,float bz,float cx,float cy,float cz,float dx,float dy,float dz)
    {
        if(count==Capacity)return;
        float ad=Depth(ay,az),bd=Depth(by,bz),cd=Depth(cy,cz),dd=Depth(dy,dz);
        if(Math.Min(Math.Min(ad,bd),Math.Min(cd,dd))<=CylinderWorld.Near)return;
        int at=count*8;
        polygons[at]=(ax-eyeX)/ad;polygons[at+1]=Up(ay,az)/ad;
        polygons[at+2]=(bx-eyeX)/bd;polygons[at+3]=Up(by,bz)/bd;
        polygons[at+4]=(cx-eyeX)/cd;polygons[at+5]=Up(cy,cz)/cd;
        polygons[at+6]=(dx-eyeX)/dd;polygons[at+7]=Up(dy,dz)/dd;
        depths[count++]=Math.Max(Math.Max(ad,bd),Math.Max(cd,dd));
    }
    public bool Hidden(float x,float y,float z,float radius)
    {
        float depth=Depth(y,z),near=depth-radius;if(near<=CylinderWorld.Near)return false;
        float far=depth+radius,side=x-eyeX,up=Up(y,z);
        float x0=Math.Min((side-radius)/near,(side-radius)/far),x1=Math.Max((side+radius)/near,(side+radius)/far);
        float y0=Math.Min((up-radius)/near,(up-radius)/far),y1=Math.Max((up+radius)/near,(up+radius)/far);
        for(int i=0;i<count;i++)if(near>depths[i]+.05f&&Contains(i,x0,y0)&&Contains(i,x1,y0)&&Contains(i,x1,y1)&&Contains(i,x0,y1))return true;
        return false;
    }
    private bool Contains(int polygon,float x,float y)
    {
        int at=polygon*8;bool positive=false,negative=false;
        for(int i=0;i<4;i++){
            int a=at+i*2,b=at+((i+1)%4)*2;
            float edge=(polygons[b]-polygons[a])*(y-polygons[a+1])-(polygons[b+1]-polygons[a+1])*(x-polygons[a]);
            if(edge>.00001f)positive=true;else if(edge<-.00001f)negative=true;else return false;
        }
        return positive!=negative;
    }
}
