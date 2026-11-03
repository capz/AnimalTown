using Dolphin.Graphics;

// Snapshot transforms/colors; never queue the game's shared mutable objects.
internal sealed class BoxBatch
{
    private const int Capacity=128;
    private readonly Mesh[] meshes=new Mesh[Capacity];
    private readonly Transform[] poses=new Transform[Capacity];
    private readonly uint[] colors=new uint[Capacity];
    private readonly Material material=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private readonly Renderer renderer;
    private int count;
    internal BoxBatch(Renderer renderer,Mesh cube,Transform root)
    {this.renderer=renderer;for(int i=0;i<Capacity;i++){meshes[i]=cube;poses[i]=new Transform{Parent=root};}}
    internal void Add(Vector3 position,Vector3 scale,Quaternion rotation,Color32 color)
    {
        if(count==Capacity||count>0&&colors[count-1]!=color.Packed)Flush();
        var pose=poses[count];pose.LocalPosition=position;pose.LocalScale=scale;pose.LocalRotation=rotation;colors[count++]=color.Packed;material.Color=color;
    }
    internal void Flush(){if(count==0)return;renderer.DrawMeshes(meshes,poses,material,0,count);count=0;}
}
