using AnimalTown;
using Dolphin.Graphics;

// One fixed dynamic stream, updated before BeginFrame. Invisible slots are
// degenerate; visible drops retain the original alpha painter order and depth test.
internal sealed class RainRenderer : System.IDisposable
{
    private DynamicMesh? mesh;
    private readonly VertexPositionColor[] vertices=new VertexPositionColor[DebugWeather.DropCount*6];
    private readonly Transform pose=new Transform();
    private bool visible;
    private readonly Material material=new Material{Blend=BlendMode.Alpha,DepthWrite=false,Cull=CullMode.Back,Lighting=LightingMode.Unlit,Color=new Color32(195,222,236,155)};
    internal void Prepare(GameState state,double seconds,float eyeHeight)
    {
        visible=false;
        for(int i=0;i<DebugWeather.DropCount;i++){
            DebugWeather.Drop(i,state.X,state.Z,seconds,out float x,out float z,out float h);
            var p=CylinderWorld.Surface(z,state.Z,TerrainElevation.At(state.Town,x,z)+h);
            bool draw=RenderView.Sphere(x,p.Y,p.Z,.8f,state.X,state.Z,eyeHeight)&&RenderView.AboveHorizon(p.Y,p.Z,.8f,state.Z,eyeHeight);
            visible|=draw;int at=i*6;
            var a=new VertexPositionColor(draw?new Vector3(x-.015f,p.Y,p.Z):Vector3.Zero,Color32.White);
            var b=new VertexPositionColor(draw?new Vector3(x-.11f,p.Y+.7f,p.Z):Vector3.Zero,Color32.White);
            var c=new VertexPositionColor(draw?new Vector3(x-.08f,p.Y+.7f,p.Z):Vector3.Zero,Color32.White);
            var d=new VertexPositionColor(draw?new Vector3(x+.015f,p.Y,p.Z):Vector3.Zero,Color32.White);
            vertices[at]=a;vertices[at+1]=b;vertices[at+2]=c;vertices[at+3]=a;vertices[at+4]=c;vertices[at+5]=d;
        }
        if(mesh==null)mesh=DynamicMesh.Create(vertices);else mesh.SetVertices(vertices);
    }
    internal void Draw(Renderer renderer){if(visible&&mesh!=null)renderer.DrawMesh(mesh,material,pose);}
    public void Dispose(){mesh?.Dispose();mesh=null;visible=false;}
}
