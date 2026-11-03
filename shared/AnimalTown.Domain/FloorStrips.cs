namespace AnimalTown;

// Curved rows merge horizontally by material; UV repeat replaces per-cell quads.
public sealed class FloorStrips
{
    public const int Capacity=1365,MaximumMaterials=52,Records=284;
    public readonly int[] X0=new int[Capacity],X1=new int[Capacity],Z0=new int[Capacity],Z1=new int[Capacity],Material=new int[Capacity];
    public int Count,RejectedQuads;
    public static int Read(byte[] data,int at)=>data[at]|data[at+1]<<8|data[at+2]<<16|data[at+3]<<24;
    public static void Validate(byte[] data)
    {
        if(data.Length<Records||Read(data,0)!=0x32525441||Read(data,4)!=1||Read(data,8)!=TerrainTiles.SurfaceCount||Read(data,12)<1||Read(data,12)>MaximumMaterials||Read(data,16)!=FloorTexture.Maximum)throw new InvalidOperationException("Invalid repeating floor metadata");
        int slots=Read(data,12),patches=Read(data,280);
        if(patches<0||patches>32||data.Length!=Records+patches*28)throw new InvalidOperationException("Invalid repeating floor patches");
        for(int s=0;s<TerrainTiles.SurfaceCount;s++){int at=20+s*20,count=Read(data,at);if(count<1||count>4)throw new InvalidOperationException("Invalid floor variant count");for(int v=0;v<4;v++){int slot=Read(data,at+4+v*4);if(v<count?(slot<0||slot>=slots):slot!= -1)throw new InvalidOperationException("Invalid floor texture slot");}}
        for(int i=0;i<patches;i++){int p=Records+i*28,s=Read(data,p),required=Read(data,p+4),x=Read(data,p+8),z=Read(data,p+12),w=Read(data,p+16),h=Read(data,p+20),border=Read(data,p+24);if(s<0||s>=TerrainTiles.SurfaceCount||required<0||required>8||x<0||z<0||border<1||w<=border*2||h<=border*2||x>Town.Width-w||z>Town.Height-h)throw new InvalidOperationException("Floor patch exceeds world");}
    }
    public static int At(GameState state,byte[] data,int x,int z,int room)
    {
        int surface=room==0?TerrainTiles.Surface(state.Town.At(x,z)):TerrainTiles.Floor;
        if(room==0)for(int i=0;i<Read(data,280);i++){int p=Records+i*28;if(Read(data,p+4)==surface&&x>=Read(data,p+8)&&z>=Read(data,p+12)&&x<Read(data,p+8)+Read(data,p+16)&&z<Read(data,p+12)+Read(data,p+20)){surface=Read(data,p);break;}}
        int at=20+surface*20,variant=TerrainTiles.Variant(x/8,z/8,surface,Read(data,at));return Read(data,at+4+variant*4);
    }
    public void Prepare(GameState state,int room,byte[] data)
    {
        Count=0;RejectedQuads=0;
        if(room!=0){int size=state.HouseTier==0?4:state.HouseTier==1?6:8;Add(0,size,0,size,At(state,data,0,0,room));return;}
        int minX=(int)state.X/2*2-CylinderWorld.ViewHalfWidth,minZ=(int)state.Z/2*2-TerrainTiles.GroundRowsAhead;
        for(int row=0;row<TerrainTiles.GroundRows;row++){
            int z=minZ+row,half=TerrainTiles.RowHalfWidth(row),left=minX+CylinderWorld.ViewHalfWidth-half,right=minX+CylinderWorld.ViewHalfWidth+half;
            if(!RenderView.SurfaceFront(z,state.Z)&&!RenderView.SurfaceFront(z+1,state.Z)){RejectedQuads+=right-left;continue;}
            var a=CylinderWorld.Surface(z,state.Z);var b=CylinderWorld.Surface(z+1,state.Z);int start=left,key=-1;
            for(int x=left;x<=right;x++){
                int next=-1;
                if(x<right){if(RenderView.Sphere(x+.5f,(a.Y+b.Y)*.5f,(a.Z+b.Z)*.5f,TerrainTiles.GroundVisibilityRadius,state.X,state.Z))next=At(state,data,x,z,room);else RejectedQuads++;}
                if(next!=key){if(key>=0)Add(start,x,z,z+1,key);start=x;key=next;}
            }
        }
    }
    private void Add(int x0,int x1,int z0,int z1,int material)
    {if(Count>=Capacity)throw new InvalidOperationException("Repeating terrain exceeds geometry budget");X0[Count]=x0;X1[Count]=x1;Z0[Count]=z0;Z1[Count]=z1;Material[Count++]=material;}
}
