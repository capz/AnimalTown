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
    private readonly StaticCylinderPlacements placements=new StaticCylinderPlacements(512);
    private readonly Transform scrollRoot;
    private bool cachedPlacement;
    internal LowPolyTrees(Transform world)
    {
        scrollRoot=new Transform{Parent=world};
        for(int i=0;i<Capacity;i++)poses[i]=new Transform{Parent=scrollRoot};
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
    internal void Prepare(Town town,float focusZ)
    {
        cachedPlacement=TextureStreaming.Enabled(16);placements.Begin(town,town.TerrainVersion,focusZ);
        var scroll=placements.Scroll;
        scrollRoot.LocalPosition=cachedPlacement?new Vector3(0,scroll.Y,scroll.Z):Vector3.Zero;
        scrollRoot.LocalRotation=cachedPlacement?new Quaternion(scroll.HalfSin,0,0,scroll.HalfCos):new Quaternion(0,0,0,1);
    }
    internal bool Visible(Town town,int index,float focusX,float focusZ,float eyeHeight)
    {
        var g=town.Ground[index];float scale=g.Kind==2?.75f:3,height=TerrainElevation.At(town,g.X,g.Z);
        var point=cachedPlacement?placements.Project(placements.Get(index,g.X,g.Z,height),scale*.5f):CylinderWorld.Surface(g.Z,focusZ,height+scale*.5f);
        return RenderView.Sphere(g.X,point.Y,point.Z,scale*1.5f,focusX,focusZ,eyeHeight)&&RenderView.AboveHorizon(point.Y,point.Z,scale*1.5f,focusZ,eyeHeight);
    }
    internal void Draw(Renderer renderer,Town town,int[] selected,float focusZ,int month,int seasonOverride=-1)
    {
        int season=TerrainTiles.Season(month,seasonOverride);
        int count=0;
        for(int n=0;n<selected.Length;n++)if(selected[n]>=0){
            int index=selected[n];var g=town.Ground[index];float scale=g.Kind==2?.75f:3,height=TerrainElevation.At(town,g.X,g.Z);
            var p=cachedPlacement?placements.Get(index,g.X,g.Z,height):new StaticCylinderPlacement(CylinderWorld.Surface(g.Z,focusZ,height));
            var pose=poses[count];pose.LocalPosition=new Vector3(g.X,p.Surface.Y,p.Surface.Z);pose.LocalScale=new Vector3(scale,scale,scale);pose.LocalRotation=new Quaternion(p.HalfSin,0,0,p.HalfCos);kinds[count++]=g.Item==3?2:g.Item==2?1:0;
        }
        for(int kind=0;kind<3;kind++){
            Color32 leaf=kind==1?new Color32(61,139,77):kind==2?new Color32(50,111,69):season==2?new Color32(184,121,49):season==3?new Color32(124,158,143):new Color32(84,147,65);
            materials[1].Color=new Color32((byte)(leaf.R*3/4),(byte)(leaf.G*3/4),(byte)(leaf.B*3/4));materials[2].Color=leaf;
            materials[3].Color=season==3&&kind!=1?new Color32(229,237,230):new Color32((byte)Math.Min(255,leaf.R+24),(byte)Math.Min(255,leaf.G+24),(byte)Math.Min(255,leaf.B+24));
            for(int part=0;part<4;part++){
            int used=0;for(int n=0;n<count;n++)if(kinds[n]==kind){batchMeshes[used]=geometry[kind*4+part];batchTransforms[used++]=poses[n];}
            if(used>0){NativeTextures.GeometryScope(1);try{renderer.DrawMeshes(batchMeshes,batchTransforms,materials[part],0,used);}finally{NativeTextures.GeometryScope(0);}}
            }
        }
    }
}
