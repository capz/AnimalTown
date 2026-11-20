using System;
using AnimalTown;
using Dolphin.Graphics;

// Shared meshes modulate season and local AO with one tiny opaque
// mipmapped detail texture. No extra pass, per-instance texture or frame upload.
internal sealed class LowPolyTrees : IDisposable
{
    internal const int Capacity=TreeDrawBatch.Capacity;
    private readonly DynamicMesh[] geometry=new DynamicMesh[LowPolyTree.KindCount];
    private readonly VertexPositionColorTexture[][] streams=new VertexPositionColorTexture[LowPolyTree.KindCount][];
    private readonly Material material=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit,Color=Color32.White};
    private Texture2D? detail;
    private int preparedSeason=-1;
    private bool preparedBloom;
    private readonly Transform[] poses=new Transform[Capacity],batchTransforms=new Transform[Capacity];
    private readonly Mesh[] batchMeshes=new Mesh[Capacity];
    private readonly int[] kinds=new int[Capacity];
    internal LowPolyTrees(Transform world)
    {
        for(int i=0;i<Capacity;i++)poses[i]=new Transform{Parent=world};
    }
    public void Dispose()
    {
        // Batch entries borrow the shared meshes; dispose each owner once.
        Array.Clear(batchMeshes,0,batchMeshes.Length);
        for(int i=0;i<geometry.Length;i++){geometry[i]?.Dispose();geometry[i]=null!;streams[i]=null!;}
        material.Texture=null;detail?.Dispose();detail=null;
        preparedSeason=-1;
    }
    internal void Prepare(int season,bool bloom)
    {
        if(detail==null){
            detail=Texture2D.FromPackage(LowPolyTree.DetailTexture(),true);
            detail.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear);
            material.Texture=detail;
        }
        bloom=bloom&&season==0;
        if(preparedSeason==season&&preparedBloom==bloom)return;
        for(int kind=0;kind<geometry.Length;kind++){
            if(preparedSeason==season&&kind!=LowPolyTree.Cherry)continue;
            var tree=LowPolyTree.Build(kind);
            var vertices=streams[kind];if(vertices==null){vertices=new VertexPositionColorTexture[tree.Triangles*3];streams[kind]=vertices;}
            WriteVertices(tree,kind,season,1,vertices,bloom);
            if(geometry[kind]==null)geometry[kind]=DynamicMesh.Create(vertices);else geometry[kind].SetVertices(vertices);
        }
        preparedSeason=season;
        preparedBloom=bloom;
    }
    internal static void WriteVertices(LowPolyTree tree,int kind,int season,float uvScale,VertexPositionColorTexture[] vertices,bool bloom=false)
    {
        Color32 leaf=kind==1?new Color32(61,139,77):kind==2?new Color32(50,111,69):season==2?new Color32(184,121,49):season==3?new Color32(124,158,143):new Color32(84,147,65);
        var colors=new[]{new Color32(123,85,49),new Color32((byte)(leaf.R*3/4),(byte)(leaf.G*3/4),(byte)(leaf.B*3/4)),leaf,season==3&&kind!=1?new Color32(229,237,230):new Color32((byte)Math.Min(255,leaf.R+24),(byte)Math.Min(255,leaf.G+24),(byte)Math.Min(255,leaf.B+24))};
        bool flowering=bloom&&kind==LowPolyTree.Cherry;
        if(flowering){
            colors[1]=new Color32(190,109,141);
            colors[2]=new Color32(231,153,180);
            colors[3]=new Color32(255,216,227);
        }
        // Keep the original part/triangle order and multiply its palette in RGB only.
        int at=0;
        for(int part=0;part<4;part++)for(int t=0;t<tree.Triangles;t++)if(tree.Parts[t]==part)for(int v=0;v<3;v++){
            int vertex=t*3+v,p=vertex*3,uv=vertex*2,ambient=tree.Ambient[vertex];
            var position=new Vector3(tree.Positions[p],tree.Positions[p+1],tree.Positions[p+2]);
            Color32 color=colors[part];
            if(flowering&&part!=0)color=BlossomColor(color,position);
            var rgb=new Color32((byte)((color.R*ambient+127)/255),(byte)((color.G*ambient+127)/255),(byte)((color.B*ambient+127)/255),color.A);
            vertices[at++]=new VertexPositionColorTexture(position,rgb,new Vector2(tree.Uvs[uv]*uvScale,tree.Uvs[uv+1]*uvScale));
        }
    }
    private static Color32 BlossomColor(Color32 color,Vector3 position)
    {
        float noise=LowPolyTree.BlossomNoise(position.X,position.Y,position.Z);
        // Keep some patches in the original pink, and blend the rest towards
        // blush or rose. GX interpolates these colors across the canopy faces.
        float amount=Math.Max(0,Math.Abs(noise)-.12f)*.85f;
        var tint=noise>0?new Color32(255,237,245):new Color32(153,67,111);
        return new Color32(
            (byte)(color.R+(tint.R-color.R)*amount+.5f),
            (byte)(color.G+(tint.G-color.G)*amount+.5f),
            (byte)(color.B+(tint.B-color.B)*amount+.5f));
    }
    internal void Draw(Renderer renderer,Town town,int[] selected,int selectedCount,float focusZ)
    {
        if(preparedSeason<0)return;
        int count=0;
        for(int n=0;n<selectedCount;n++){var g=town.Ground[selected[n]];float scale=g.Kind==2?.75f:3;var p=CylinderWorld.Surface(g.Z,focusZ,TerrainElevation.At(town,g.X,g.Z));float angle=p.Pitch*.5f;var pose=poses[count];pose.LocalPosition=new Vector3(g.X,p.Y,p.Z);pose.LocalScale=new Vector3(scale,scale,scale);pose.LocalRotation=new Quaternion((float)Math.Sin(angle),0,0,(float)Math.Cos(angle));kinds[count++]=g.Item switch{Catalog.CherrySapling=>LowPolyTree.Cherry,Catalog.PineSapling=>2,2=>1,_=>0};}
        for(int kind=0;kind<geometry.Length;kind++){
            int used=WriteBatch(geometry[kind],kind,kinds,poses,count,batchMeshes,batchTransforms);
            if(used>0)renderer.DrawMeshes(batchMeshes,batchTransforms,material,0,used);
        }
    }
    internal static int WriteBatch(Mesh geometry,int kind,int[] kinds,Transform[] poses,int count,Mesh[] meshes,Transform[] transforms)
    {
        int used=0;
        for(int n=0;n<count;n++)if(kinds[n]==kind){meshes[used]=geometry;transforms[used++]=poses[n];}
        return used;
    }
}
