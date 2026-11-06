using System;
using AnimalTown;
using Dolphin.Graphics;

// One indexed, uniform-colour mesh for the dominant neutral floor material.
// UVs stay in logical world coordinates, including the cloud-shadow layer.
internal sealed class GroundRingRenderer : IDisposable
{
    private readonly GroundRingPlan plan=new GroundRingPlan();
    private readonly CylinderRows rows=new CylinderRows();
    private readonly VertexPositionColorTexture[] vertices=new VertexPositionColorTexture[GroundRingPlan.VertexCount];
    private readonly VertexPositionColorTexture[] row=new VertexPositionColorTexture[GroundRingPlan.Columns*4];
    private DynamicMesh? mesh;
    internal int ResidentBytes=>vertices.Length*24+row.Length*24+GroundRingPlan.Rows*4+GroundRingPlan.Columns*4+(CylinderRows.Maximum-CylinderRows.Minimum+1)*20+(mesh?.NativeBytes??0);
    internal bool Uploaded {get;private set;}
    internal void Prepare(GameState state,byte[] data,int material,bool invalidate)
    {
        plan.Prepare(state.X,state.Z,invalidate);Uploaded=plan.RowCount>0||plan.ColumnCount>0;
        if(!Uploaded)return;
        for(int n=0;n<plan.RowCount;n++)for(int x=plan.MinX;x<plan.MinX+GroundRingPlan.Columns;x++)Cell(state,data,x,plan.EnteringRows[n],material,rows);
        if(!plan.Reset)for(int n=0;n<plan.ColumnCount;n++)for(int z=plan.MinZ;z<plan.MinZ+GroundRingPlan.Rows;z++)Cell(state,data,plan.EnteringColumns[n],z,material,rows);
        if(mesh==null){
            var indices=new ushort[GroundRingPlan.IndexCount];
            for(int cell=0;cell<GroundRingPlan.Columns*GroundRingPlan.Rows;cell++){
                int v=cell*4,i=cell*6;indices[i]=(ushort)v;indices[i+1]=(ushort)(v+1);indices[i+2]=(ushort)(v+2);indices[i+3]=(ushort)v;indices[i+4]=(ushort)(v+2);indices[i+5]=(ushort)(v+3);
            }
            mesh=DynamicMesh.Create(vertices,indices);
        }else if(plan.Reset||plan.ColumnCount>0||plan.RowCount>1){
            // The SDK rescans colour and flushes the native allocation on each
            // update. Coalesce scattered column edits into one upload.
            mesh.SetVertices(vertices);
        }else for(int n=0;n<plan.RowCount;n++){
            int at=GroundRingPlan.Wrap(plan.EnteringRows[n],GroundRingPlan.Rows)*row.Length;
            Array.Copy(vertices,at,row,0,row.Length);mesh.SetVertices(row,at,row.Length);
        }
    }
    private void Cell(GameState state,byte[] data,int x,int z,int material,CylinderRows rows)
    {
        int at=GroundRingPlan.Vertex(x,z),slot=FloorStrips.At(state,data,x,z,0);
        if(!GroundRingPlan.Uses(state.Town,x,z,slot,material)){
            var empty=new VertexPositionColorTexture(Vector3.Zero,Color32.White,Vector2.Zero);
            for(int i=0;i<4;i++)vertices[at+i]=empty;return;
        }
        var a=rows.At(z);var b=rows.At(z+1);
        vertices[at]=V(x,a,x*.5f,z*.5f);vertices[at+1]=V(x+1,a,(x+1)*.5f,z*.5f);
        vertices[at+2]=V(x+1,b,(x+1)*.5f,(z+1)*.5f);vertices[at+3]=V(x,b,x*.5f,(z+1)*.5f);
    }
    private static VertexPositionColorTexture V(float x,CylinderPose p,float u,float v)=>new VertexPositionColorTexture(new Vector3(x,p.Y,p.Z),Color32.White,new Vector2(u,v));
    internal void Draw(Renderer renderer,Material material,Transform transform){if(mesh!=null)renderer.DrawMesh(mesh,material,transform);}
    public void Dispose(){mesh?.Dispose();mesh=null;}
}
