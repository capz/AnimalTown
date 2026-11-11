using System;
using AnimalTown;
using Dolphin.Graphics;

// One vertex-colored indexed mesh per species. White material tint keeps the
// SDK indexed path; seasonal resources change only between frames.
internal sealed class LowPolyTrees : IDisposable
{
    internal const int Capacity=32;
    private readonly Mesh[] geometry=new Mesh[3];
    private readonly Material material=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit,Color=Color32.White};
    private int preparedSeason=-1;
    private readonly Transform[] poses=new Transform[Capacity],batchTransforms=new Transform[Capacity];
    private readonly Mesh[] batchMeshes=new Mesh[Capacity];
    private readonly int[] kinds=new int[Capacity];
    internal LowPolyTrees(Transform world)
    {
        for(int i=0;i<Capacity;i++)poses[i]=new Transform{Parent=world};
    }
    public void Dispose()
    {
        // Batch entries borrow the three shared meshes; dispose each owner once.
        Array.Clear(batchMeshes,0,batchMeshes.Length);
        for(int i=0;i<geometry.Length;i++){geometry[i]?.Dispose();geometry[i]=null!;}
        preparedSeason=-1;
    }
    internal void Prepare(int season)
    {
        if(preparedSeason==season)return;
        for(int kind=0;kind<3;kind++){
            var tree=LowPolyTree.Build(kind);
            Color32 leaf=kind==1?new Color32(61,139,77):kind==2?new Color32(50,111,69):season==2?new Color32(184,121,49):season==3?new Color32(124,158,143):new Color32(84,147,65);
            var colors=new[]{new Color32(123,85,49),new Color32((byte)(leaf.R*3/4),(byte)(leaf.G*3/4),(byte)(leaf.B*3/4)),leaf,season==3&&kind!=1?new Color32(229,237,230):new Color32((byte)Math.Min(255,leaf.R+24),(byte)Math.Min(255,leaf.G+24),(byte)Math.Min(255,leaf.B+24))};
            var vertices=new VertexPositionColor[tree.Triangles*3];
            // Retain the old part/triangle order, including the same face colors.
            int at=0;for(int part=0;part<4;part++)for(int t=0;t<tree.Triangles;t++)if(tree.Parts[t]==part)for(int v=0;v<3;v++){
                int p=t*9+v*3;vertices[at++]=new VertexPositionColor(new Vector3(tree.Positions[p],tree.Positions[p+1],tree.Positions[p+2]),colors[part]);
            }
            var replacement=Mesh.Create(vertices);geometry[kind]?.Dispose();geometry[kind]=replacement;
        }
        preparedSeason=season;
    }
    internal void Draw(Renderer renderer,Town town,int[] selected,float focusZ)
    {
        if(preparedSeason<0)return;
        int count=0;
        for(int n=0;n<selected.Length;n++)if(selected[n]>=0){var g=town.Ground[selected[n]];float scale=g.Kind==2?.75f:3;var p=CylinderWorld.Surface(g.Z,focusZ,TerrainElevation.At(town,g.X,g.Z));float angle=p.Pitch*.5f;var pose=poses[count];pose.LocalPosition=new Vector3(g.X,p.Y,p.Z);pose.LocalScale=new Vector3(scale,scale,scale);pose.LocalRotation=new Quaternion((float)Math.Sin(angle),0,0,(float)Math.Cos(angle));kinds[count++]=g.Item==3?2:g.Item==2?1:0;}
        for(int kind=0;kind<3;kind++){
            int used=0;for(int n=0;n<count;n++)if(kinds[n]==kind){batchMeshes[used]=geometry[kind];batchTransforms[used++]=poses[n];}
            if(used>0)renderer.DrawMeshes(batchMeshes,batchTransforms,material,0,used);
        }
    }
}
