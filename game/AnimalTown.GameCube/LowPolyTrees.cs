using System;
using AnimalTown;
using Dolphin.Graphics;

// Shared, texture-free, uniform-color meshes take the SDK's indexed GX path.
internal sealed class LowPolyTrees
{
    internal const int Capacity=32;
    private readonly Mesh[] geometry=new Mesh[12];
    private readonly Material[] materials=new Material[4];
    private readonly Transform[] poses=new Transform[Capacity],batchTransforms=new Transform[Capacity];
    private readonly Mesh[] batchMeshes=new Mesh[Capacity];
    private readonly int[] kinds=new int[Capacity];
    internal LowPolyTrees(Transform world)
    {
        for(int i=0;i<Capacity;i++)poses[i]=new Transform{Parent=world};
        for(int p=0;p<4;p++)materials[p]=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
        materials[0].Color=new Color32(123,85,49);
        for(int kind=0;kind<3;kind++){
            var tree=LowPolyTree.Build(kind);
            for(int part=0;part<4;part++){
                int count=0;for(int t=0;t<tree.Triangles;t++)if(tree.Parts[t]==part)count+=3;
                var vertices=new VertexPositionColor[count];int at=0;
                for(int t=0;t<tree.Triangles;t++)if(tree.Parts[t]==part)for(int v=0;v<3;v++){int p=t*9+v*3;vertices[at++]=new VertexPositionColor(new Vector3(tree.Positions[p],tree.Positions[p+1],tree.Positions[p+2]),Color32.White);}
                geometry[kind*4+part]=Mesh.Create(vertices);
            }
        }
    }
    internal void Draw(Renderer renderer,Town town,int[] selected,float focusZ,int month)
    {
        int season=TerrainTiles.Season(month);
        int count=0;
        for(int n=0;n<selected.Length;n++)if(selected[n]>=0){var g=town.Ground[selected[n]];float scale=g.Kind==2?.75f:3;var p=CylinderWorld.Surface(g.Z,focusZ);float angle=p.Pitch*.5f;var pose=poses[count];pose.LocalPosition=new Vector3(g.X,p.Y,p.Z);pose.LocalScale=new Vector3(scale,scale,scale);pose.LocalRotation=new Quaternion((float)Math.Sin(angle),0,0,(float)Math.Cos(angle));kinds[count++]=g.Item==3?2:g.Item==2?1:0;}
        for(int kind=0;kind<3;kind++){
            Color32 leaf=kind==1?new Color32(61,139,77):kind==2?new Color32(50,111,69):season==2?new Color32(184,121,49):season==3?new Color32(124,158,143):new Color32(84,147,65);
            materials[1].Color=new Color32((byte)(leaf.R*3/4),(byte)(leaf.G*3/4),(byte)(leaf.B*3/4));materials[2].Color=leaf;
            materials[3].Color=season==3&&kind!=1?new Color32(229,237,230):new Color32((byte)Math.Min(255,leaf.R+24),(byte)Math.Min(255,leaf.G+24),(byte)Math.Min(255,leaf.B+24));
            for(int part=0;part<4;part++){
            int used=0;for(int n=0;n<count;n++)if(kinds[n]==kind){batchMeshes[used]=geometry[kind*4+part];batchTransforms[used++]=poses[n];}
            if(used>0)renderer.DrawMeshes(batchMeshes,batchTransforms,materials[part],0,used);
            }
        }
    }
}
