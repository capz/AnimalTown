using AnimalTown;
using Dolphin.Graphics;

// Only exposed block/ramp sides are emitted. Coplanar section faces disappear;
// all corners use the same cylinder/elevation function as the floor mesh.
internal sealed class TerrainWalls
{
    private readonly VertexPositionColor[] vertices=new VertexPositionColor[3072];
    private readonly Material material=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit,Color=new Color32(138,120,87)};
    private DynamicMesh? mesh;private Town? town;private float x,z;private int version=-1,used;
    internal void Prepare(GameState state,int room)
    {
        if(room!=0){used=0;version=-1;return;}
        if(town==state.Town&&version==state.Town.TerrainVersion&&x==state.X&&z==state.Z)return;
        town=state.Town;version=town.TerrainVersion;x=state.X;z=state.Z;used=0;float eye=TerrainElevation.At(town,x,z);
        int minZ=(int)z-TerrainTiles.GroundRowsAhead;
        for(int row=0;row<TerrainTiles.GroundRows;row++)for(int cx=(int)x-TerrainTiles.RowHalfWidth(row);cx<=(int)x+TerrainTiles.RowHalfWidth(row);cx++){
            int cz=minZ+row;if(TerrainElevation.Mode(town,cx,cz)==0)continue;var p=CylinderWorld.Surface(cz+.5f,z,1);
            if(!RenderView.Sphere(cx+.5f,p.Y,p.Z,3.5f,x,z,eye))continue;
            Edge(cx,cz,cx,cz,cx+1,cz,cx,cz-1);Edge(cx,cz,cx+1,cz,cx+1,cz+1,cx+1,cz);
            Edge(cx,cz,cx+1,cz+1,cx,cz+1,cx,cz+1);Edge(cx,cz,cx,cz+1,cx,cz,cx-1,cz);
        }
        if(used>0){for(int i=used;i<vertices.Length;i++)vertices[i]=new VertexPositionColor(Vector3.Zero,Color32.White);if(mesh==null)mesh=DynamicMesh.Create(vertices);else mesh.SetVertices(vertices);}
    }
    private Vector3 Point(float px,float pz,float h){var p=CylinderWorld.Surface(pz,z,h);return new Vector3(px,p.Y,p.Z);}
    private void Edge(int cx,int cz,int ax,int az,int bx,int bz,int nx,int nz)
    {
        float a=TerrainElevation.AtCell(town!,cx,cz,ax,az),b=TerrainElevation.AtCell(town!,cx,cz,bx,bz),la=TerrainElevation.AtCell(town!,nx,nz,ax,az),lb=TerrainElevation.AtCell(town!,nx,nz,bx,bz);
        if(a<=la+.001f&&b<=lb+.001f)return;if(used+6>vertices.Length)throw new System.InvalidOperationException("Raised terrain sides exceed budget");
        var p0=Point(ax,az,a);var p1=Point(bx,bz,b);var p2=Point(bx,bz,System.Math.Min(b,lb));var p3=Point(ax,az,System.Math.Min(a,la));var c=Color32.White;
        vertices[used++]=new VertexPositionColor(p0,c);vertices[used++]=new VertexPositionColor(p2,c);vertices[used++]=new VertexPositionColor(p1,c);
        vertices[used++]=new VertexPositionColor(p0,c);vertices[used++]=new VertexPositionColor(p3,c);vertices[used++]=new VertexPositionColor(p2,c);
    }
    internal void Draw(Renderer renderer,Transform parent){if(used>0&&mesh!=null)renderer.DrawMesh(mesh,material,parent);}
}
