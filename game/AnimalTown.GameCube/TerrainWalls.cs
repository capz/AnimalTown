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
    private DynamicMeshBuffer soilMesh=new DynamicMeshBuffer(3072);
    private long revision;
    private readonly VertexPositionColorTexture[]?[] ground=new VertexPositionColorTexture[]?[FloorStrips.MaximumMaterials];
    private readonly DynamicMeshBuffer?[] groundMesh=new DynamicMeshBuffer?[FloorStrips.MaximumMaterials];
    private readonly int[] used=new int[FloorStrips.MaximumMaterials];
    private readonly CylinderRows rows;
    private int soilUsed,total;
    internal TerrainWalls(CylinderRows rows){this.rows=rows;}
    internal Mesh? SoilMesh=>soilMesh.Mesh;
    internal Mesh? GroundMesh(int slot)=>groundMesh[slot]?.Mesh;
    internal int ResidentBytes {get{int n=soilMesh.Capacity*24+(SoilMesh?.NativeBytes??0);for(int i=0;i<groundMesh.Length;i++)if(groundMesh[i]!=null)n+=groundMesh[i]!.Capacity*24+(groundMesh[i]!.Mesh?.NativeBytes??0);return n;}}
    private Town? town;
    internal int Triangles=>total/3;
    internal ulong MaterialMask {get;private set;}
    internal void Build(GameState state,int ox,int oz,int size,TerrainRenderer floor,bool preview,int previewX,int previewZ)
    {
        town=state.Town;soilUsed=total=0;for(int i=0;i<used.Length;i++)used[i]=0;
        for(int cz=oz;cz<oz+size;cz++)for(int cx=ox;cx<ox+size;cx++){
            int mode=WaterTerrain.IsCoast(town.At(cx,cz))?1:TerrainElevation.Mode(town,cx,cz);if(WaterSurface.IsWater(town,cx,cz))continue;
            if(mode>=TerrainElevation.CornerUpLeft){if(cx%2==0&&cz%2==0)PlacedSection(state,floor,mode-2,cx,cz);continue;}
            int slot=floor.GroundSlot(state,cx,cz);
            Edge(cx,cz,cx,cz,cx+1,cz,cx,cz-1,slot);Edge(cx,cz,cx+1,cz,cx+1,cz+1,cx+1,cz,slot);
            Edge(cx,cz,cx+1,cz+1,cx,cz+1,cx,cz+1,slot);Edge(cx,cz,cx,cz+1,cx,cz,cx-1,cz,slot);
        }
        if(preview)for(int i=0;i<8;i++){
            TerrainCliffBinding.PreviewOrigin(previewX,previewZ,i,out int px,out int pz);
            if(px>=ox&&px<ox+size&&pz>=oz&&pz<oz+size)Section(state,floor,i,px,pz);
        }
        revision++;MaterialMask=0;
        if(soilUsed>0)floor.EnsureSoil();
        if(soilUsed==0&&soilMesh.Capacity>0){soilMesh.Dispose();soilMesh=new DynamicMeshBuffer(3072);soil=new VertexPositionColorTexture[0];}
        else soilMesh.Commit(soilUsed,revision,FrameProfile.Counters);
        for(int i=0;i<used.Length;i++){
            if(used[i]==0){groundMesh[i]?.Dispose();groundMesh[i]=null;ground[i]=null;}
            else {MaterialMask|=1UL<<i;groundMesh[i]!.Commit(used[i],revision,FrameProfile.Counters);}
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
    private Vector3 Point(float px,float pz,float h){var p=rows.Chord(pz,h);return new Vector3(px,p.Y,p.Z);}
    private VertexPositionColorTexture Vertex(float px,float pz,float h,float u,float v)=>new VertexPositionColorTexture(Point(px,pz,h),Color32.White,new Vector2(u,v));
    private void Add(int slot,VertexPositionColorTexture a,VertexPositionColorTexture b,VertexPositionColorTexture c)
    {
        if(total+3>Capacity)throw new InvalidOperationException("Cliff geometry exceeds batch budget");total+=3;
        if(slot<0){soil=GeometryStaging.Reserve(soilMesh,soil,soilUsed,soilUsed+3,FrameProfile.Counters);soil[soilUsed++]=a;soil[soilUsed++]=b;soil[soilUsed++]=c;return;}
        int at=used[slot];if(groundMesh[slot]==null)groundMesh[slot]=new DynamicMeshBuffer(Capacity);
        var buffer=ground[slot]=GeometryStaging.Reserve(groundMesh[slot]!,ground[slot]??new VertexPositionColorTexture[0],at,at+3,FrameProfile.Counters);
        buffer[at++]=a;buffer[at++]=b;buffer[at++]=c;used[slot]=at;
    }
    private void Edge(int cx,int cz,int ax,int az,int bx,int bz,int nx,int nz,int slot)
    {
        if(town!.At(cx,cz)==(int)Terrain.Bridge)return;
        if(town.At(cx,cz)==(int)Terrain.Beach&&WaterTerrain.IsSea(town,nx,nz))return;
        float a=TerrainElevation.AtCell(town!,cx,cz,ax,az),b=TerrainElevation.AtCell(town!,cx,cz,bx,bz),la=TerrainElevation.AtCell(town!,nx,nz,ax,az),lb=TerrainElevation.AtCell(town!,nx,nz,bx,bz);
        if(a<=la+.001f&&b<=lb+.001f)return;la=Math.Min(a,la);lb=Math.Min(b,lb);
        float sa=Math.Max(la,a-TerrainCliffGeometry.LipDepth),sb=Math.Max(lb,b-TerrainCliffGeometry.LipDepth);
        Wall(ax,az,bx,bz,sa,sb,la,lb,-1);Wall(ax,az,bx,bz,a,b,sa,sb,slot);
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
            // Ordinary ground already supplies the zero-height half; omit its
            // duplicate coplanar triangle and reuse the floor material directly.
            if(g.Parts[t]==TerrainCliffGeometry.Low)continue;int slot=g.Parts[t]==TerrainCliffGeometry.Soil?-1:groundSlot;
            int ia=g.Indices[t*3],ib=g.Indices[t*3+2],ic=g.Indices[t*3+1];Add(slot,TemplateVertex(g,ia,ox,oz,slot<0),TemplateVertex(g,ib,ox,oz,slot<0),TemplateVertex(g,ic,ox,oz,slot<0));
        }
    }
    private VertexPositionColorTexture TemplateVertex(TerrainCliffGeometry g,int i,int ox,int oz,bool wall)
    {float px=ox+g.Positions[i*3],pz=oz+g.Positions[i*3+2];return Vertex(px,pz,g.Positions[i*3+1],wall?g.Uvs[i*2]:TerrainCliffGeometry.SpillUv(px,Math.Max(0,2-g.Positions[i*3+1]),g.Normals[i*3]),wall?g.Uvs[i*2+1]:TerrainCliffGeometry.SpillUv(pz,Math.Max(0,2-g.Positions[i*3+1]),g.Normals[i*3+2]));}
    internal void Dispose(){soilMesh.Dispose();for(int i=0;i<groundMesh.Length;i++)groundMesh[i]?.Dispose();}
}
