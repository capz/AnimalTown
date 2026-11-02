using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;

// One active seasonal RGB565 atlas. Reusable meshes are updated before BeginFrame.
internal sealed class TerrainRenderer
{
    private readonly VertexPositionColorTexture[] outside=new VertexPositionColorTexture[TerrainTiles.GroundVertexCount];
    private readonly VertexPositionColorTexture[] inside=new VertexPositionColorTexture[8*8*6];
    private readonly DynamicMesh outdoorMesh,indoorMesh;
    private readonly Material material=new Material{Cull=CullMode.None,Lighting=LightingMode.Unlit};
    private byte[] data=new byte[0];
    private Texture2D? texture;
    private int season=-1,forced,size,pixels,stride,columns,patches,previousInside;
    private int cachedX=int.MinValue,cachedZ,cachedRoom=-1,cachedTier=-1;
    private float cachedFocus;
    private Town? cachedTown;
    private const int RecordsEnd=28+TerrainTiles.SurfaceCount*12;
    internal TerrainRenderer()
    {
        var cfg=File.ReadAllBytes("dvd:/terrain.cfg");if(cfg.Length!=4)throw new IOException("Invalid terrain configuration");
        forced=cfg[0]|cfg[1]<<8|cfg[2]<<16|cfg[3]<<24;
        if(forced< -1||forced>3)throw new IOException("Invalid forced terrain season");
        outdoorMesh=DynamicMesh.Create(outside);indoorMesh=DynamicMesh.Create(inside);
    }
    private int Read(int at)=>data[at]|data[at+1]<<8|data[at+2]<<16|data[at+3]<<24;
    internal void Prepare(GameState state,int room,int month)
    {
        int next=TerrainTiles.Season(month,forced);bool changed=next!=season;
        if(next!=season)
        {
            string name="dvd:/terrain-"+TerrainTiles.SeasonName(next);
            data=File.ReadAllBytes(name+".bin");
            if(data.Length<RecordsEnd||Read(0)!=0x314e5254||Read(4)!=1||Read(20)!=TerrainTiles.SurfaceCount)throw new IOException("Invalid terrain atlas metadata");
            size=Read(8);pixels=Read(12);stride=Read(16);patches=Read(24);columns=size/stride;
            if((size!=256&&size!=512)||(pixels!=32&&pixels!=64)||stride!=pixels+4||patches<0||patches>32||data.Length!=RecordsEnd+patches*28)throw new IOException("Terrain atlas bounds invalid");
            for(int s=0;s<TerrainTiles.SurfaceCount;s++){int at=28+s*12,mode=Read(at),count=Read(at+4),first=Read(at+8);if(mode<0||mode>1||count<1||count>4||first<0||first+(mode==1?9:count)>columns*columns)throw new IOException("Terrain material bounds invalid");}
            for(int i=0;i<patches;i++){int at=RecordsEnd+i*28,s=Read(at),required=Read(at+4),x=Read(at+8),z=Read(at+12),w=Read(at+16),h=Read(at+20),b=Read(at+24);if(s<0||s>=TerrainTiles.SurfaceCount||required<0||required>8||x<0||z<0||b<1||w<=b*2||h<=b*2||x>Town.Width-w||z>Town.Height-h)throw new IOException("Terrain patch bounds invalid");}
            var replacement=Texture2D.LoadAsync(name+".dndtex",default,true).GetAwaiter().GetResult();
            if(replacement.Width!=size||replacement.Height!=size){replacement.Dispose();throw new IOException("Terrain texture/metadata size mismatch");}
            texture?.Dispose();texture=replacement;material.Texture=texture;season=next;
        }
        int minX=(int)state.X/2*2-CylinderWorld.ViewHalfWidth,maxX=minX+128,minZ=(int)state.Z/2*2-48,maxZ=minZ+68;
        if(!changed&&cachedX==minX&&cachedZ==minZ&&cachedFocus==state.Z&&cachedRoom==room&&cachedTier==state.HouseTier&&cachedTown==state.Town)return;
        cachedX=minX;cachedZ=minZ;cachedFocus=state.Z;cachedRoom=room;cachedTier=state.HouseTier;cachedTown=state.Town;
        var vertices=outside;
        if(room!=0){minX=0;minZ=0;maxX=state.HouseTier==0?4:state.HouseTier==1?6:8;maxZ=maxX;vertices=inside;}
        int write=0;
        for(int z=minZ;z<maxZ;z++)
        {
            var a=CylinderWorld.Surface(z,state.Z);var b=CylinderWorld.Surface(z+1,state.Z);
            int left=minX,right=maxX;
            if(room==0){int half=TerrainTiles.RowHalfWidth(z-minZ);left=minX+CylinderWorld.ViewHalfWidth-half;right=minX+CylinderWorld.ViewHalfWidth+half;}
            for(int x=left;x<right;x++)
            {
                int surface=room==0?TerrainTiles.Surface(state.Town.At(x,z)):TerrainTiles.Floor;
                Uv(x,z,surface,out float u0,out float v0,out float u1,out float v1);
                vertices[write++]=new VertexPositionColorTexture(new Vector3(x,a.Y,a.Z),Color32.White,new Vector2(u0,v0));
                vertices[write++]=new VertexPositionColorTexture(new Vector3(x+1,b.Y,b.Z),Color32.White,new Vector2(u1,v1));
                vertices[write++]=new VertexPositionColorTexture(new Vector3(x+1,a.Y,a.Z),Color32.White,new Vector2(u1,v0));
                vertices[write++]=new VertexPositionColorTexture(new Vector3(x,a.Y,a.Z),Color32.White,new Vector2(u0,v0));
                vertices[write++]=new VertexPositionColorTexture(new Vector3(x,b.Y,b.Z),Color32.White,new Vector2(u0,v1));
                vertices[write++]=new VertexPositionColorTexture(new Vector3(x+1,b.Y,b.Z),Color32.White,new Vector2(u1,v1));
            }
        }
        if(room==0)outdoorMesh.SetVertices(outside);
        else{for(int i=write;i<previousInside;i++)inside[i]=default;previousInside=write;indoorMesh.SetVertices(inside);}
    }
    private void Uv(int x,int z,int surface,out float u0,out float v0,out float u1,out float v1)
    {
        int patch=-1;
        for(int i=0;i<patches;i++){int p=RecordsEnd+i*28;if(Read(p+4)==surface&&x>=Read(p+8)&&z>=Read(p+12)&&x<Read(p+8)+Read(p+16)&&z<Read(p+12)+Read(p+20)){patch=p;surface=Read(p);break;}}
        int at=28+surface*12,slot=Read(at+8);float x0=0,z0=0,x1=1,z1=1;
        if(Read(at)==0)slot+=TerrainTiles.Variant(x,z,surface,Read(at+4));
        else
        {
            if(patch>=0){int px=Read(patch+8),pz=Read(patch+12),w=Read(patch+16),h=Read(patch+20),border=Read(patch+24);x0=TerrainTiles.PatchCoordinate(x-px,w,border);x1=TerrainTiles.PatchCoordinate(x+1-px,w,border);z0=TerrainTiles.PatchCoordinate(z-pz,h,border);z1=TerrainTiles.PatchCoordinate(z+1-pz,h,border);int sx=Math.Min(2,(int)((x0+x1)*.5f)),sz=Math.Min(2,(int)((z0+z1)*.5f));slot+=sz*3+sx;x0-=sx;x1-=sx;z0-=sz;z1-=sz;}
            else slot+=4;
        }
        float left=slot%columns*stride+2+.5f,top=slot/columns*stride+2+.5f;
        u0=(left+x0*(pixels-1))/size;u1=(left+x1*(pixels-1))/size;v0=(top+z0*(pixels-1))/size;v1=(top+z1*(pixels-1))/size;
    }
    internal void Draw(Renderer renderer,Transform parent,int room)=>renderer.DrawMesh(room==0?outdoorMesh:indoorMesh,material,parent);
}
