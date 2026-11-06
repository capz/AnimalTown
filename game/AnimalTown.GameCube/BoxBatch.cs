using Dolphin.Graphics;
using Hypercube.GameCube;

// All box coordinates are projected into world space before submission. No
// parent is applied: TownGame's worldRoot is an identity transform.
internal sealed class BoxBatch
{
    private readonly MeshBatch batch=new MeshBatch(128,1,1);
    private readonly Renderer renderer;
    private readonly int mesh,material;
    internal BoxBatch(Renderer renderer,Mesh cube)
    {this.renderer=renderer;mesh=batch.RegisterMesh(cube);material=batch.RegisterMaterial(new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit});}
    internal void Add(Vector3 position,Vector3 scale,Quaternion rotation,Color32 color)
    {
        if(batch.Count==batch.Capacity)Flush();
        if(!batch.TryAdd(mesh,material,position,rotation,scale,color,FrameProfile.Counters))throw new System.InvalidOperationException("Box batch full after flush");
    }
    internal void Flush()=>batch.Flush(renderer,FrameProfile.Counters);
}
