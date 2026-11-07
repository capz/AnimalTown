namespace AnimalTown;

// Curved rows merge horizontally by material; UV repeat replaces per-cell quads.
public sealed class FloorStrips
{
    public const int Capacity=2048,MaximumMaterials=52,Records=284;
    public readonly int[] X0,X1,Z0,Z1,Material;
    public readonly byte[] Elevation;
    public readonly bool[] Water;
    public int Count,RejectedQuads;
    public FloorStrips(int capacity=Capacity)
    {X0=new int[capacity];X1=new int[capacity];Z0=new int[capacity];Z1=new int[capacity];Material=new int[capacity];Elevation=new byte[capacity];Water=new bool[capacity];}
    public static int Read(byte[] data,int at)=>data[at]|data[at+1]<<8|data[at+2]<<16|data[at+3]<<24;
    private static int MaterialOffset(byte[] data,int slot)=>Records+Read(data,280)*28+slot*8;
    public static int TextureSlot(byte[] data,int slot)=>Read(data,4)==1?slot:Read(data,MaterialOffset(data,slot));
    public static uint Tint(byte[] data,int slot)=>Read(data,4)==1?0xffffffffu:(uint)Read(data,MaterialOffset(data,slot)+4);
    public static void Validate(byte[] data)
    {
        if(data.Length<Records||Read(data,0)!=0x32525441||(Read(data,4)!=1&&Read(data,4)!=2)||Read(data,8)!=TerrainTiles.SurfaceCount||Read(data,12)<1||Read(data,12)>MaximumMaterials||Read(data,16)!=FloorTexture.Maximum)throw new InvalidOperationException("Invalid repeating floor metadata");
        int slots=Read(data,12),patches=Read(data,280);
        if(patches<0||patches>32||data.Length!=Records+patches*28+(Read(data,4)==2?slots*8:0))throw new InvalidOperationException("Invalid repeating floor patches");
        if(Read(data,4)==2)for(int i=0;i<slots;i++)if(TextureSlot(data,i)<0||TextureSlot(data,i)>1||(Tint(data,i)&255)!=255)throw new InvalidOperationException("Invalid shared floor material");
        for(int s=0;s<TerrainTiles.SurfaceCount;s++){int at=20+s*20,count=Read(data,at);if(count<1||count>4)throw new InvalidOperationException("Invalid floor variant count");for(int v=0;v<4;v++){int slot=Read(data,at+4+v*4);if(v<count?(slot<0||slot>=slots):slot!= -1)throw new InvalidOperationException("Invalid floor texture slot");}}
        for(int i=0;i<patches;i++){int p=Records+i*28,s=Read(data,p),required=Read(data,p+4),x=Read(data,p+8),z=Read(data,p+12),w=Read(data,p+16),h=Read(data,p+20),border=Read(data,p+24);if(s<0||s>=TerrainTiles.SurfaceCount||required<0||required>8||x<0||z<0||border<1||w<=border*2||h<=border*2||x>Town.Width-w||z>Town.Height-h)throw new InvalidOperationException("Floor patch exceeds world");}
    }
    public static int At(GameState state,byte[] data,int x,int z,int room)
    {
        int surface=room==0?TerrainTiles.Surface(state.Town.At(x,z)):TerrainTiles.Floor;
        if(room==0)for(int i=0;i<Read(data,280);i++){int p=Records+i*28;if(Read(data,p+4)==surface&&x>=Read(data,p+8)&&z>=Read(data,p+12)&&x<Read(data,p+8)+Read(data,p+16)&&z<Read(data,p+12)+Read(data,p+20)){surface=Read(data,p);break;}}
        int at=20+surface*20,variant=TerrainTiles.Variant(x/8,z/8,surface,Read(data,at));return Read(data,at+4+variant*4);
    }
    public void Prepare(GameState state,int room,byte[] data,bool cliffPreview=false,bool scrollWindow=false)
    {
        Count=0;RejectedQuads=0;
        if(room!=0){int size=state.HouseTier==0?4:state.HouseTier==1?6:8;Add(0,size,0,size,At(state,data,0,0,room));return;}
        float focusX=scrollWindow?CylinderWorld.ScrollAnchor(state.X):state.X,focusZ=scrollWindow?CylinderWorld.ScrollAnchor(state.Z):state.Z;
        float guard=scrollWindow?CylinderWorld.ScrollGuard:0;
        int minX=(int)state.X/2*2-CylinderWorld.ViewHalfWidth,minZ=(int)state.Z/2*2-TerrainTiles.GroundRowsAhead;
        float focusHeight=scrollWindow?1:TerrainElevation.At(state.Town,state.X,state.Z);
        for(int row=0;row<TerrainTiles.GroundRows;row++){
            int z=minZ+row,half=TerrainTiles.RowHalfWidth(row),left=minX+CylinderWorld.ViewHalfWidth-half,right=minX+CylinderWorld.ViewHalfWidth+half;
            bool front=scrollWindow?
                RenderView.SurfaceFront(z,focusZ-1,2)||RenderView.SurfaceFront(z+1,focusZ-1,2)||RenderView.SurfaceFront(z,focusZ+1,2)||RenderView.SurfaceFront(z+1,focusZ+1,2):
                RenderView.SurfaceFront(z,focusZ,focusHeight)||RenderView.SurfaceFront(z+1,focusZ,focusHeight);
            if(!front){RejectedQuads+=right-left;continue;}
            var a=CylinderWorld.Surface(z,focusZ);var b=CylinderWorld.Surface(z+1,focusZ);int start=left,key=-1;
            for(int x=left;x<=right;x++){
                int next=-1;
                if(x<right){
                    int mode=(uint)x<Town.Width&&(uint)z<Town.Height?state.Town.Elevation[z*Town.Width+x]:0;
                    if(WaterTerrain.IsCoast(state.Town.At(x,z))&&!WaterSurface.IsWater(state.Town,x,z))mode=1;
                    float minH=mode==1?2:0,maxH=minH;
                    if(mode>=2){float h0=TerrainElevation.AtCell(state.Town,x,z,x,z),h1=TerrainElevation.AtCell(state.Town,x,z,x+1,z),h2=TerrainElevation.AtCell(state.Town,x,z,x,z+1),h3=TerrainElevation.AtCell(state.Town,x,z,x+1,z+1);minH=Math.Min(Math.Min(h0,h1),Math.Min(h2,h3));maxH=Math.Max(Math.Max(h0,h1),Math.Max(h2,h3));}
                    float h=(minH+maxH)*.5f;
                    if(mode>=TerrainElevation.CornerUpLeft&&!WaterSurface.IsWater(state.Town,x,z)){} // Caps are the two level halves of the cliff template, never a sloping quad.
                    else if(RenderView.Sphere(x+.5f,(a.Y+b.Y+h*(a.UpY+b.UpY))*.5f,(a.Z+b.Z+h*(a.UpZ+b.UpZ))*.5f,TerrainTiles.GroundVisibilityRadius+guard+(maxH-minH)*.5f,focusX,focusZ,focusHeight)){
                        int neighbor=cliffPreview?TerrainCliffBinding.PreviewLowNeighbor(state.Town,(int)focusX,(int)focusZ,x,z):-1;
                        next=(neighbor<0?At(state,data,x,z,room):At(state,data,neighbor%Town.Width,neighbor/Town.Width,room))+(mode+(WaterSurface.IsWater(state.Town,x,z)?8:0))*MaximumMaterials;
                    }else RejectedQuads++;
                }
                if(next==key&&key>=0&&((key/MaximumMaterials)%8>=2&&x%2==0||state.Town.At(x-1,z)==(int)Terrain.Beach||state.Town.At(x,z)==(int)Terrain.Beach)){Add(start,x,z,z+1,key);start=x;}
                if(next!=key){if(key>=0)Add(start,x,z,z+1,key);start=x;key=next;}
            }
        }
    }
    public void PrepareRegion(GameState state,byte[] data,int left,int top,int width,int height)
    {
        Count=RejectedQuads=0;
        for(int z=top;z<top+height;z++){
            int start=left,key=-1;
            for(int x=left;x<=left+width;x++){
                int next=-1;
                if(x<left+width){int mode=TerrainElevation.Mode(state.Town,x,z);bool water=WaterSurface.IsWater(state.Town,x,z);
                    if(WaterTerrain.IsCoast(state.Town.At(x,z))&&!water)mode=1;
                    if(mode<TerrainElevation.CornerUpLeft||water)next=At(state,data,x,z,0)+(mode+(water?8:0))*MaximumMaterials;
                }
                if(next==key&&key>=0&&((key/MaximumMaterials)%8>=2&&x%2==0||state.Town.At(x-1,z)==(int)Terrain.Beach||state.Town.At(x,z)==(int)Terrain.Beach)){Add(start,x,z,z+1,key);start=x;}
                if(next!=key){if(key>=0)Add(start,x,z,z+1,key);start=x;key=next;}
            }
        }
    }
    private void Add(int x0,int x1,int z0,int z1,int material)
    {if(Count>=X0.Length)throw new InvalidOperationException("Repeating terrain exceeds geometry budget");X0[Count]=x0;X1[Count]=x1;Z0[Count]=z0;Z1[Count]=z1;Material[Count]=material%MaximumMaterials;Water[Count]=material/MaximumMaterials>=8;Elevation[Count++]=(byte)(material/MaximumMaterials%8);}
}
