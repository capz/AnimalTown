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
        for(int row=0;row<TerrainTiles.GroundRows;row++)for(int cx=(int)x-TerrainTiles.RowHalfWidth(row);cx<=(int)x+TerrainTiles.RowHalfWidth(row);cx++){
            int cz=minZ+row;if(TerrainElevation.Mode(town,cx,cz)==0)continue;var p=CylinderWorld.Surface(cz+.5f,z,1);
            if(!RenderView.Sphere(cx+.5f,p.Y,p.Z,3.5f,x,z,eye))continue;
            int slot=floor.GroundSlot(state,cx,cz);
            Edge(cx,cz,cx,cz,cx+1,cz,cx,cz-1,slot);Edge(cx,cz,cx+1,cz,cx+1,cz+1,cx+1,cz,slot);
            Edge(cx,cz,cx+1,cz+1,cx,cz+1,cx,cz+1,slot);Edge(cx,cz,cx,cz+1,cx,cz,cx-1,cz,slot);
        }
        if(preview)for(int i=0;i<8;i++)Section(state,floor,i,(int)x/2*2-5+i%4*3,(int)z/2*2-11+i/4*5);
        if(soilUsed>0){if(texture==null){texture=TextureCache.LoadFloor("dvd:/cliff-soil.dndtex",repeat:true,repeatV:false);material.Texture=texture.Pages[0];}Pad(soil,soilUsed);if(soilSize!=soil.Length){soilMesh?.Dispose();soilMesh=DynamicMesh.Create(soil);soilSize=soil.Length;}else soilMesh!.SetVertices(soil);}
        for(int i=0;i<used.Length;i++)if(used[i]>0){var buffer=ground[i]!;Pad(buffer,used[i]);if(sizes[i]!=buffer.Length){groundMesh[i]?.Dispose();groundMesh[i]=DynamicMesh.Create(buffer);sizes[i]=buffer.Length;}else groundMesh[i]!.SetVertices(buffer);}
    }
    private static void Pad(VertexPositionColorTexture[] buffer,int start){for(int i=start;i<buffer.Length;i++)buffer[i]=new VertexPositionColorTexture(Vector3.Zero,Color32.White,Vector2.Zero);}
    private Vector3 Point(float px,float pz,float h){var p=CylinderWorld.Surface(pz,z,h);return new Vector3(px,p.Y,p.Z);}
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
        float a=TerrainElevation.AtCell(town!,cx,cz,ax,az),b=TerrainElevation.AtCell(town!,cx,cz,bx,bz),la=TerrainElevation.AtCell(town!,nx,nz,ax,az),lb=TerrainElevation.AtCell(town!,nx,nz,bx,bz);
        if(a<=la+.001f&&b<=lb+.001f)return;la=Math.Min(a,la);lb=Math.Min(b,lb);
        float sa=Math.Max(la,a-TerrainCliffGeometry.LipDepth),sb=Math.Max(lb,b-TerrainCliffGeometry.LipDepth);
        Wall(ax,az,bx,bz,sa,sb,la,lb,-1);Wall(ax,az,bx,bz,a,b,sa,sb,slot);
    }
    private void Wall(float ax,float az,float bx,float bz,float a,float b,float la,float lb,int slot)
    {
        float u0=ax==bx?az*.5f:ax*.5f,u1=ax==bx?bz*.5f:bx*.5f;
        var p0=slot<0?Vertex(ax,az,a,u0,(1.94f-a)/1.94f):Vertex(ax,az,a,ax*.5f,az*.5f);
        var p1=slot<0?Vertex(bx,bz,b,u1,(1.94f-b)/1.94f):Vertex(bx,bz,b,bx*.5f,bz*.5f);
        var p2=slot<0?Vertex(bx,bz,lb,u1,(1.94f-lb)/1.94f):Vertex(bx,bz,lb,bx*.5f,bz*.5f);
        var p3=slot<0?Vertex(ax,az,la,u0,(1.94f-la)/1.94f):Vertex(ax,az,la,ax*.5f,az*.5f);
        Add(slot,p0,p2,p1);Add(slot,p0,p3,p2);
    }
    private void Section(GameState state,TerrainRenderer floor,int orientation,int ox,int oz)
    {
        var g=TerrainCliffGeometry.For(orientation);int groundSlot=floor.GroundSlot(state,ox,oz);
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
