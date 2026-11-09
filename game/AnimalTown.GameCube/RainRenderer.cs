using System;
using AnimalTown;
using Dolphin.Graphics;

// Fixed shared geometry/transform pool. No simulation or resource work when off.
// Draw in the outdoor 3D pass so roofs, trees, actors and terrain occlude rain.
internal sealed class RainRenderer
{
    private Mesh? mesh;
    private readonly Mesh[] meshes=new Mesh[DebugWeather.DropCount];
    private readonly Transform[] poses=new Transform[DebugWeather.DropCount];
    private readonly Material material=new Material{Blend=BlendMode.Alpha,DepthWrite=false,Cull=CullMode.Back,Lighting=LightingMode.Unlit,Color=new Color32(195,222,236,155)};
    internal RainRenderer(){for(int i=0;i<poses.Length;i++)poses[i]=new Transform();}
    internal void Draw(Renderer renderer,GameState state,double seconds,float eyeHeight)
    {
        // First enable is prepared before BeginFrame by Prepare; never allocate here.
        if(mesh==null)return;int count=0;
        for(int i=0;i<DebugWeather.DropCount;i++){
            DebugWeather.Drop(i,state.X,state.Z,seconds,out float x,out float z,out float h);
            float ground=TerrainElevation.At(state.Town,x,z);
            var p=CylinderWorld.Surface(z,state.Z,ground+h);
            if(!RenderView.Sphere(x,p.Y,p.Z,.8f,state.X,state.Z,eyeHeight)||!RenderView.AboveHorizon(p.Y,p.Z,.8f,state.Z,eyeHeight))continue;
            poses[count].LocalPosition=new Vector3(x,p.Y,p.Z);meshes[count++]=mesh;
        }
        if(count>0)renderer.DrawMeshes(meshes,poses,material,0,count);
    }
    internal void Prepare()
    {
        if(mesh!=null)return;
        // Clockwise toward the camera (+Z), including the slanted leading edge.
        var a=new VertexPositionColor(new Vector3(-.015f,0,0),Color32.White);
        var b=new VertexPositionColor(new Vector3(-.11f,.7f,0),Color32.White);
        var c=new VertexPositionColor(new Vector3(-.08f,.7f,0),Color32.White);
        var d=new VertexPositionColor(new Vector3(.015f,0,0),Color32.White);
        mesh=Mesh.Create(new[]{a,b,c,a,c,d});
    }
}
