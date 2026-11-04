using System;
using AnimalTown;
using Dolphin.Graphics;

// Pooled soil and shared ground-material batches. No per-section textures,
// model loads, hidden bottoms or internal walls.
internal sealed class TerrainWalls
{
    private const int Capacity=6144;
    private VertexPositionColorTexture[] soil=new VertexPositionColorTexture[192];
    private readonly VertexPositionColorTexture[]?[] ground=new VertexPositionColorTexture[]?[FloorStrips.MaximumMaterials];
    private readonly DynamicMesh?[] groundMesh=new DynamicMesh?[FloorStrips.MaximumMaterials];
    private readonly int[] used=new int[FloorStrips.MaximumMaterials],sizes=new int[FloorStrips.MaximumMaterials];
    private readonly Material material=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private CachedTexture? texture;private DynamicMesh? soilMesh;private int soilUsed,total,soilSize;
    private Town? town;private float x,z;private int version=-1,materials=-1;private bool gallery;
    internal int Triangles=>total/3;
    internal void Prepare(GameState state,int room,TerrainRenderer floor,bool preview)
    {
        if(room!=0){soilUsed=total=0;for(int i=0;i<used.Length;i++)used[i]=0;version=-1;return;}
        if(town==state.Town&&version==state.Town.TerrainVersion&&x==state.X&&z==state.Z&&materials==floor.MaterialRevision&&gallery==preview)return;
        town=state.Town;version=town.TerrainVersion;x=state.X;z=state.Z;materials=floor.MaterialRevision;gallery=preview;soilUsed=total=0;for(int i=0;i<used.Length;i++)used[i]=0;
        float eye=TerrainElevation.At(town,x,z);int minZ=(int)z-TerrainTiles.GroundRowsAhead;
        for(int row=-2;row<TerrainTiles.GroundRows+2;row++)for(int cx=(int)x-TerrainTiles.RowHalfWidth(Math.Max(0,Math.Min(TerrainTiles.GroundRows-1,row)))-2;cx<=(int)x+TerrainTiles.RowHalfWidth(Math.Max(0,Math.Min(TerrainTiles.GroundRows-1,row)))+2;cx++){
            int cz=minZ+row,mode=WaterTerrain.IsCoast(town.At(cx,minZ+row))?1:TerrainElevation.Mode(town,cx,cz);if(WaterSurface.IsWater(town,cx,cz))continue;var p=CylinderWorld.Surface(cz+.5f,z,1);
            if(!RenderView.Sphere(cx+.5f,p.Y,p.Z,3.5f,x,z,eye))continue;
            if(mode>=TerrainElevation.CornerUpLeft){if(cx%2==0&&cz%2==0)PlacedSection(state,floor,mode-2,cx,cz);continue;}
            int slot=floor.GroundSlot(state,cx,cz);
            Edge(cx,cz,cx,cz,cx+1,cz,cx,cz-1,slot);Edge(cx,cz,cx+1,cz,cx+1,cz+1,cx+1,cz,slot);
            Edge(cx,cz,cx+1,cz+1,cx,cz+1,cx,cz+1,slot);Edge(cx,cz,cx,cz+1,cx,cz,cx-1,cz,slot);
        }
        if(preview)for(int i=0;i<8;i++){TerrainCliffBinding.PreviewOrigin((int)x,(int)z,i,out int ox,out int oz);Section(state,floor,i,ox,oz);}
        if(soilUsed>0){if(texture==null){texture=TextureCache.LoadFloor("dvd:/cliff-soil.dndtex",repeat:true,repeatV:false);material.Texture=texture.Pages[0];}Pad(soil,soilUsed);if(soilSize!=soil.Length){soilMesh?.Dispose();soilMesh=DynamicMesh.Create(soil);soilSize=soil.Length;}else soilMesh!.SetVertices(soil);}
        for(int i=0;i<used.Length;i++)if(used[i]>0){var buffer=ground[i]!;Pad(buffer,used[i]);if(sizes[i]!=buffer.Length){groundMesh[i]?.Dispose();groundMesh[i]=DynamicMesh.Create(buffer);sizes[i]=buffer.Length;}else groundMesh[i]!.SetVertices(buffer);}
    }
    private static void Pad(VertexPositionColorTexture[] buffer,int start){for(int i=start;i<buffer.Length;i++)buffer[i]=new VertexPositionColorTexture(Vector3.Zero,Color32.White,Vector2.Zero);}
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
        if(slot<0){if(soilUsed+3>soil.Length){int n=soil.Length*2;if(n>3072)throw new InvalidOperationException("Cliff soil exceeds batch budget");var next=new VertexPositionColorTexture[n];Array.Copy(soil,next,soilUsed);soil=next;}soil[soilUsed++]=a;soil[soilUsed++]=b;soil[soilUsed++]=c;return;}
        int at=used[slot];var buffer=ground[slot];if(buffer==null||buffer.Length<at+3){int n=buffer==null?192:buffer.Length;while(n<at+3)n*=2;var next=new VertexPositionColorTexture[n];if(buffer!=null)Array.Copy(buffer,next,at);ground[slot]=buffer=next;}
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
        float u0=ax==bx?az*.5f:ax*.5f,u1=ax==bx?bz*.5f:bx*.5f,origin=Math.Max(a,b);
        var p0=slot<0?Vertex(ax,az,a,u0,(origin-a)/1.94f):Vertex(ax,az,a,ax*.5f,az*.5f);
        var p1=slot<0?Vertex(bx,bz,b,u1,(origin-b)/1.94f):Vertex(bx,bz,b,bx*.5f,bz*.5f);
        var p2=slot<0?Vertex(bx,bz,lb,u1,(origin-lb)/1.94f):Vertex(bx,bz,lb,bx*.5f,bz*.5f);
        var p3=slot<0?Vertex(ax,az,la,u0,(origin-la)/1.94f):Vertex(ax,az,la,ax*.5f,az*.5f);
        Add(slot,p0,p2,p1);Add(slot,p0,p3,p2);
    }
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
    {float px=ox+g.Positions[i*3],pz=oz+g.Positions[i*3+2];return Vertex(px,pz,g.Positions[i*3+1],wall?g.Uvs[i*2]:px*.5f,wall?g.Uvs[i*2+1]:pz*.5f);}
    internal void Draw(Renderer renderer,Transform parent,TerrainRenderer floor)
    {if(soilUsed>0&&soilMesh!=null)renderer.DrawMesh(soilMesh,material,parent);for(int i=0;i<used.Length;i++)if(used[i]>0)renderer.DrawMesh(groundMesh[i]!,floor.GroundMaterial(i),parent);}
}
