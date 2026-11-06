using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Pooled soil and shared ground-material batches. No per-section textures,
// model loads, hidden bottoms or internal walls.
internal sealed class TerrainWalls
{
    private const int Capacity=6144;
    private VertexPositionColorTexture[] soil=new VertexPositionColorTexture[0];
    private DynamicMeshBuffer? soilMesh;
    private long revision;
    private readonly VertexPositionColorTexture[]?[] ground=new VertexPositionColorTexture[]?[FloorStrips.MaximumMaterials];
    private readonly DynamicMeshBuffer?[] groundMesh=new DynamicMeshBuffer?[FloorStrips.MaximumMaterials];
    private readonly int[] used=new int[FloorStrips.MaximumMaterials];
    private readonly TerrainSoil shared;
    internal TerrainWalls(TerrainSoil shared){this.shared=shared;}
    private int soilUsed,total;
    private Town? town;private float x,z;private int version=-1,materials=-1;private bool gallery;
    internal int Triangles=>total/3;
    internal Mesh? SoilMesh=>soilUsed==0?null:soilMesh?.Mesh;
    internal Mesh? GroundMesh(int slot)=>used[slot]==0?null:groundMesh[slot]?.Mesh;
    internal void PrepareRegion(GameState state,TerrainRenderer floor,int left,int top)
    {
        town=state.Town;z=0;soilUsed=total=0;for(int i=0;i<used.Length;i++)used[i]=0;
        for(int cz=top;cz<top+TerrainStream.Height;cz++)for(int cx=left;cx<left+TerrainStream.Width;cx++){
            int mode=WaterTerrain.IsCoast(town.At(cx,cz))?1:TerrainElevation.Mode(town,cx,cz);
            if(town.At(cx,cz)==(int)Terrain.Water){RiverCell(state,floor,cx,cz);continue;}
            if(WaterSurface.IsWater(town,cx,cz))continue;
            if(mode>=TerrainElevation.CornerUpLeft){if(cx%2==0&&cz%2==0)PlacedSection(state,floor,mode-2,cx,cz);continue;}
            int slot=floor.GroundSlot(state,cx,cz);
            Edge(cx,cz,cx,cz,cx+1,cz,cx,cz-1,slot);Edge(cx,cz,cx+1,cz,cx+1,cz+1,cx+1,cz,slot);
            Edge(cx,cz,cx+1,cz+1,cx,cz+1,cx,cz+1,slot);Edge(cx,cz,cx,cz+1,cx,cz,cx-1,cz,slot);
        }
        Commit();
    }
    internal void PrepareGallery(GameState state,TerrainRenderer floor,bool preview)
    {
        int px=(int)CylinderWorld.ScrollAnchor(state.X),pz=(int)CylinderWorld.ScrollAnchor(state.Z);
        if(town==state.Town&&x==px&&version==pz&&materials==floor.MaterialRevision&&gallery==preview)return;
        town=state.Town;x=px;version=pz;materials=floor.MaterialRevision;gallery=preview;z=0;
        soilUsed=total=0;for(int i=0;i<used.Length;i++)used[i]=0;
        if(preview)for(int i=0;i<8;i++){TerrainCliffBinding.PreviewOrigin(px,pz,i,out int ox,out int oz);Section(state,floor,i,ox,oz);}
        Commit();
    }
    private void Commit()
    {
        revision++;
        if(soilUsed>0)shared.Prepare();
        if(soilUsed>0)soilMesh!.Commit(soilUsed,revision,FrameProfile.Counters);
        else if(soilMesh!=null){soilMesh.Dispose();soilMesh=null;soil=new VertexPositionColorTexture[0];}
        for(int i=0;i<used.Length;i++){
            if(used[i]>0)groundMesh[i]!.Commit(used[i],revision,FrameProfile.Counters);
            else if(groundMesh[i]!=null){groundMesh[i]!.Dispose();groundMesh[i]=null;ground[i]=null;}
        }
    }
    private void PlacedSection(GameState state,TerrainRenderer floor,int orientation,int ox,int oz)
    {
        var g=TerrainCliffGeometry.For(orientation);
        int upper=TerrainCliffBinding.Neighbor(state.Town,orientation,ox,oz,true),lower=TerrainCliffBinding.Neighbor(state.Town,orientation,ox,oz,false);
        int highSlot=floor.GroundSlot(state,upper%Town.Width,upper/Town.Width),lowSlot=floor.GroundSlot(state,lower%Town.Width,lower/Town.Width);
        for(int t=0;t<g.Triangles;t++){
            int part=g.Parts[t];
            if(part==TerrainCliffGeometry.Top||part==TerrainCliffGeometry.Low){
                var a=Local(g,g.Indices[t*3],ox,oz);var b=Local(g,g.Indices[t*3+2],ox,oz);var c=Local(g,g.Indices[t*3+1],ox,oz);
                var ab=Mid(a,b);var bc=Mid(b,c);var ca=Mid(c,a);int slot=part==TerrainCliffGeometry.Top?highSlot:lowSlot;
                Cap(slot,a,ab,ca);Cap(slot,ab,b,bc);Cap(slot,ca,bc,c);Cap(slot,ab,bc,ca);
            }
            if(part!=TerrainCliffGeometry.Soil||t>0&&g.Parts[t-1]==TerrainCliffGeometry.Soil)continue;
            int ia=g.Indices[t*3],ib=g.Indices[t*3+1];var start=Local(g,ia,ox,oz);var end=Local(g,ib,ox,oz);
            // Split at the fine-cell row boundary before cylinder bending.
            // Hidden sides join adjacent terrace caps without internal walls.
            for(int half=0;half<2;half++){
                float ax=start.X+(end.X-start.X)*half*.5f,az=start.Z+(end.Z-start.Z)*half*.5f;
                float bx=start.X+(end.X-start.X)*(half+1)*.5f,bz=start.Z+(end.Z-start.Z)*(half+1)*.5f;
                if(TerrainElevation.At(town!,(ax+bx)*.5f+g.Normals[ia*3]*.01f,(az+bz)*.5f+g.Normals[ia*3+2]*.01f)>=2)continue;
                Wall(ax,az,bx,bz,2-TerrainCliffGeometry.LipDepth,2-TerrainCliffGeometry.LipDepth,0,0,-1);
                Wall(ax,az,bx,bz,2,2,2-TerrainCliffGeometry.LipDepth,2-TerrainCliffGeometry.LipDepth,highSlot);
            }
        }
    }
    private static Vector3 Local(TerrainCliffGeometry g,int i,int ox,int oz)=>new Vector3(ox+g.Positions[i*3],g.Positions[i*3+1],oz+g.Positions[i*3+2]);
    private static Vector3 Mid(Vector3 a,Vector3 b)=>new Vector3((a.X+b.X)*.5f,(a.Y+b.Y)*.5f,(a.Z+b.Z)*.5f);
    private VertexPositionColorTexture GroundVertex(Vector3 p)=>Vertex(p.X,p.Z,p.Y,p.X*.5f,p.Z*.5f);
    private void Cap(int slot,Vector3 a,Vector3 b,Vector3 c)=>Add(slot,GroundVertex(a),GroundVertex(b),GroundVertex(c));
    private Vector3 Point(float px,float pz,float h){int row=(int)Math.Floor(pz);var a=CylinderWorld.Surface(row,z,h);var b=CylinderWorld.Surface(row+1,z,h);float t=pz-row;return new Vector3(px,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);}
    private VertexPositionColorTexture Vertex(float px,float pz,float h,float u,float v)=>new VertexPositionColorTexture(Point(px,pz,h),Color32.White,new Vector2(u,v));
    private void Add(int slot,VertexPositionColorTexture a,VertexPositionColorTexture b,VertexPositionColorTexture c)
    {
        if(total+3>Capacity)throw new InvalidOperationException("Cliff geometry exceeds batch budget");total+=3;
        if(slot<0){if(soilMesh==null)soilMesh=new DynamicMeshBuffer(3072);soil=GeometryStaging.Reserve(soilMesh,soil,soilUsed,soilUsed+3,FrameProfile.Counters);soil[soilUsed++]=a;soil[soilUsed++]=b;soil[soilUsed++]=c;return;}
        int at=used[slot];if(groundMesh[slot]==null){
            for(int old=0;old<used.Length;old++)if(used[old]==0&&groundMesh[old]!=null){groundMesh[slot]=groundMesh[old];ground[slot]=ground[old];groundMesh[old]=null;ground[old]=null;break;}
            if(groundMesh[slot]==null)groundMesh[slot]=new DynamicMeshBuffer(Capacity);
        }
        var buffer=ground[slot]=GeometryStaging.Reserve(groundMesh[slot]!,ground[slot]??new VertexPositionColorTexture[0],at,at+3,FrameProfile.Counters);
        buffer[at++]=a;buffer[at++]=b;buffer[at++]=c;used[slot]=at;
    }
    private void Edge(int cx,int cz,int ax,int az,int bx,int bz,int nx,int nz,int slot)
    {
        if(town!.At(cx,cz)==(int)Terrain.Bridge)return;
        if(town.At(cx,cz)==(int)Terrain.Beach&&WaterTerrain.IsSea(town,nx,nz))return;
        int riverSide=cz<nz?0:cx>nx?1:cz>nz?2:3;
        if(RiverBankGeometry.HasEdge(town,nx,nz,riverSide))return;
        float a=TerrainElevation.AtCell(town!,cx,cz,ax,az),b=TerrainElevation.AtCell(town!,cx,cz,bx,bz),la=TerrainElevation.AtCell(town!,nx,nz,ax,az),lb=TerrainElevation.AtCell(town!,nx,nz,bx,bz);
        if(a<=la+.001f&&b<=lb+.001f)return;la=Math.Min(a,la);lb=Math.Min(b,lb);
        float sa=Math.Max(la,a-TerrainCliffGeometry.LipDepth),sb=Math.Max(lb,b-TerrainCliffGeometry.LipDepth);
        Wall(ax,az,bx,bz,sa,sb,la,lb,-1);Wall(ax,az,bx,bz,a,b,sa,sb,slot);
    }
    private VertexPositionColorTexture RiverVertex(RiverBankPoint p,bool soil,float top)
        =>Vertex(p.X,p.Z,p.Height,soil?(p.X+p.Z)*.5f:p.X*.5f,soil?(top-p.Height)/1.94f:p.Z*.5f);
    private void RiverCell(GameState state,TerrainRenderer floor,int cx,int cz)
    {
        for(int side=0;side<4;side++)if(RiverBankGeometry.Edge(town!,cx,cz,side,out var a,out var b,out var c,out var d)){
            int slot=floor.GroundSlot(state,RiverBankGeometry.NeighborX(cx,side),RiverBankGeometry.NeighborZ(cz,side));
            var la=RiverBankGeometry.Lip(a,d);var lb=RiverBankGeometry.Lip(b,c);
            Add(slot,RiverVertex(a,false,0),RiverVertex(b,false,0),RiverVertex(lb,false,0));Add(slot,RiverVertex(a,false,0),RiverVertex(lb,false,0),RiverVertex(la,false,0));
            Add(-1,RiverVertex(la,true,a.Height),RiverVertex(lb,true,b.Height),RiverVertex(c,true,b.Height));Add(-1,RiverVertex(la,true,a.Height),RiverVertex(c,true,b.Height),RiverVertex(d,true,a.Height));
        }
        for(int corner=0;corner<4;corner++)if(RiverBankGeometry.Corner(town!,cx,cz,corner,out var a,out var b,out var c)){
            int nx=cx+(corner==0||corner==3?-1:1),nz=cz+(corner<2?-1:1),slot=floor.GroundSlot(state,nx,nz);
            var lb=RiverBankGeometry.Lip(a,b);var lc=RiverBankGeometry.Lip(a,c);
            Add(slot,RiverVertex(a,false,0),RiverVertex(lb,false,0),RiverVertex(lc,false,0));
            Add(-1,RiverVertex(lb,true,a.Height),RiverVertex(b,true,a.Height),RiverVertex(c,true,a.Height));Add(-1,RiverVertex(lb,true,a.Height),RiverVertex(c,true,a.Height),RiverVertex(lc,true,a.Height));
        }
        for(int corner=0;corner<4;corner++)if(RiverBankGeometry.Join(town!,cx,cz,corner,out var a,out var b,out var c)){
            // Close a height discontinuity between two adjacent dry banks.
            float top=Math.Max(a.Height,b.Height);Add(-1,RiverVertex(a,true,top),RiverVertex(b,true,top),RiverVertex(c,true,top));Add(-1,RiverVertex(a,true,top),RiverVertex(c,true,top),RiverVertex(b,true,top));
        }
    }
    private void Wall(float ax,float az,float bx,float bz,float a,float b,float la,float lb,int slot)
    {
        float origin=Math.Max(a,b);
        float u0=ax==bx?az*.5f:ax*.5f,u1=ax==bx?bz*.5f:bx*.5f;
        float dx=bx-ax,dz=bz-az,length=(float)Math.Sqrt(dx*dx+dz*dz),nx=dz/length,nz=-dx/length;
        var p0=slot<0?Vertex(ax,az,a,u0,(origin-a)/1.94f):LipVertex(ax,az,a,a,nx,nz);
        var p1=slot<0?Vertex(bx,bz,b,u1,(origin-b)/1.94f):LipVertex(bx,bz,b,b,nx,nz);
        var p2=slot<0?Vertex(bx,bz,lb,u1,(origin-lb)/1.94f):LipVertex(bx,bz,lb,b,nx,nz);
        var p3=slot<0?Vertex(ax,az,la,u0,(origin-la)/1.94f):LipVertex(ax,az,la,a,nx,nz);
        Add(slot,p0,p2,p1);Add(slot,p0,p3,p2);
    }
    private VertexPositionColorTexture LipVertex(float px,float pz,float height,float top,float nx,float nz)
        =>Vertex(px,pz,height,TerrainCliffGeometry.SpillUv(px,top-height,nx),TerrainCliffGeometry.SpillUv(pz,top-height,nz));
    private void Section(GameState state,TerrainRenderer floor,int orientation,int ox,int oz)
    {
        var g=TerrainCliffGeometry.For(orientation);int neighbor=TerrainCliffBinding.Neighbor(state.Town,orientation,ox,oz,true);
        int groundSlot=floor.GroundSlot(state,neighbor%Town.Width,neighbor/Town.Width);
        for(int t=0;t<g.Triangles;t++){
            int slot=g.Parts[t]==TerrainCliffGeometry.Soil?-1:g.Parts[t]==TerrainCliffGeometry.Low?floor.GroundSlot(state,neighbor%Town.Width,neighbor/Town.Width):groundSlot;
            int ia=g.Indices[t*3],ib=g.Indices[t*3+2],ic=g.Indices[t*3+1];Add(slot,TemplateVertex(g,ia,ox,oz,slot<0),TemplateVertex(g,ib,ox,oz,slot<0),TemplateVertex(g,ic,ox,oz,slot<0));
        }
    }
    private VertexPositionColorTexture TemplateVertex(TerrainCliffGeometry g,int i,int ox,int oz,bool wall)
    {float px=ox+g.Positions[i*3],pz=oz+g.Positions[i*3+2],h=g.Positions[i*3+1];return Vertex(px,pz,h==0&&!wall?.003f:h,wall?g.Uvs[i*2]:TerrainCliffGeometry.SpillUv(px,Math.Max(0,2-h),g.Normals[i*3]),wall?g.Uvs[i*2+1]:TerrainCliffGeometry.SpillUv(pz,Math.Max(0,2-h),g.Normals[i*3+2]));}
    internal void Draw(Renderer renderer,Transform parent,TerrainRenderer floor)
    {if(soilUsed>0&&soilMesh?.Mesh!=null)renderer.DrawMesh(soilMesh.Mesh!,shared.Material,parent);for(int i=0;i<used.Length;i++)if(used[i]>0)renderer.DrawMesh(groundMesh[i]!.Mesh!,floor.GroundMaterial(i),parent);}
}

internal sealed class TerrainSoil
{
    internal readonly Material Material=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private CachedTexture? texture;
    internal void Prepare(){if(texture!=null)return;texture=TextureCache.LoadFloor("dvd:/cliff-soil.dndtex",repeat:true,repeatV:false);Material.Texture=texture.Pages[0];}
}
