using System;
using AnimalTown;
using Dolphin.Graphics;

// One shared billboard mesh, six fixed transforms. Debug-only visual attention;
// dialogue, requests and NPC journeys are untouched. Normal depth/culling apply.
internal sealed class VillagerAttentionRenderer : IDisposable
{
    private Mesh? mesh;
    private Texture2D? noise;
    private int preparedNoise=-1;
    private readonly float[] heights=new float[6],joint=new float[16];
    internal void Register(int species,ModelInstance instance)
    {
        float top=0;for(int node=0;node<instance.Model.NodeCount;node++){instance.GetNodeTransform(node,joint);if(float.IsFinite(joint[13]))top=Math.Max(top,joint[13]);}
        heights[species]=RenderDebugOptions.AttentionHeight(top);
    }
    private readonly Mesh[] meshes=new Mesh[6];
    private readonly Transform[] poses=new Transform[6];
    private readonly Material material=new Material{Lighting=LightingMode.Unlit,Blend=BlendMode.Alpha,DepthWrite=false,DepthTest=true,Cull=CullMode.Back};
    internal VillagerAttentionRenderer(){for(int i=0;i<poses.Length;i++)poses[i]=new Transform();}
    internal void Prepare(){
        if(mesh==null)mesh=Mesh.Create(BuildVertices());
        int percent=UiNoiseSettings.Percent;if(preparedNoise==percent)return;
        Texture2D? replacement=percent==0?null:Texture2D.FromPackage(LowPolyTree.DetailTexture(percent,uniform:true),true);
        replacement?.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear);
        material.Texture=replacement;noise?.Dispose();noise=replacement;preparedNoise=percent;
    }
    public void Dispose(){Array.Clear(meshes,0,meshes.Length);mesh?.Dispose();mesh=null;material.Texture=null;noise?.Dispose();noise=null;preparedNoise=-1;}
    internal static VertexPositionColorTexture[] BuildVertices()
    {
        var vertices=new VertexPositionColorTexture[132];int at=0;
        void Triangle(float ax,float ay,float bx,float by,float cx,float cy,float z,Color32 color){
            vertices[at++]=new VertexPositionColorTexture(new Vector3(ax,ay,z),color,new Vector2(ax*1.5f,ay*1.5f));vertices[at++]=new VertexPositionColorTexture(new Vector3(bx,by,z),color,new Vector2(bx*1.5f,by*1.5f));vertices[at++]=new VertexPositionColorTexture(new Vector3(cx,cy,z),color,new Vector2(cx*1.5f,cy*1.5f));
        }
        void Disc(float cx,float cy,float rx,float ry,float z,Color32 color,int count){
            for(int i=0;i<count;i++){float a=-i*6.2831853f/count,b=-(i+1)*6.2831853f/count;Triangle(cx,cy,cx+rx*(float)Math.Cos(a),cy+ry*(float)Math.Sin(a),cx+rx*(float)Math.Cos(b),cy+ry*(float)Math.Sin(b),z,color);}
        }
        var shadow=new Color32(48,65,58,110);var paper=new Color32(255,245,221);var ink=new Color32(49,72,62);
        Disc(.025f,-.025f,.43f,.24f,0,shadow,16);Triangle(-.12f,-.14f,.08f,-.14f,-.19f,-.35f,0,shadow);
        Disc(0,0,.43f,.24f,.01f,paper,16);Triangle(-.15f,-.12f,.05f,-.12f,-.22f,-.33f,.01f,paper);
        Triangle(-.026f,-.035f,-.026f,.14f,.026f,.14f,.02f,ink);Triangle(-.026f,-.035f,.026f,.14f,.026f,-.035f,.02f,ink);
        Disc(0,-.11f,.028f,.028f,.02f,ink,8);
        if(at!=132)throw new InvalidOperationException("Attention icon exceeds its shared mesh budget");
        return vertices;
    }
    internal void Draw(Renderer renderer,GameState state,ActorFrameVisibility view,InteriorCamera camera,float eyeHeight)
    {
        if(mesh==null)return;int count=0,room=state.Space;float angle=room==0?0:camera.Angle;
        var rotation=new Quaternion(0,(float)Math.Sin(angle*.5f),0,(float)Math.Cos(angle*.5f));
        for(int actor=1;actor<BuildingWorld.Actors;actor++){
            var location=view.Locations[actor];if(location.Space.Value!=room)continue;
            float x=location.X,y=heights[actor-1],z=location.Z;
            if(room==0){var p=CylinderWorld.Surface(z,state.Z,TerrainElevation.At(state.Town,x,z)+y);y=p.Y;z=p.Z;
                if(!RenderView.Sphere(x,y,z,.6f,state.X,state.Z,eyeHeight)||!RenderView.AboveHorizon(y,z,.6f,state.Z,eyeHeight))continue;
            }else if(!camera.Sphere(x,y,z,.6f,BuildingWorld.Size(state,room)))continue;
            var pose=poses[count];pose.LocalPosition=new Vector3(x,y,z);pose.LocalRotation=rotation;meshes[count++]=mesh;
        }
        if(count>0)renderer.DrawMeshes(meshes,poses,material,0,count);
    }
}
