using System;
using AnimalTown;
using Dolphin.Graphics;

internal sealed class FallLeafWake : IDisposable
{
    internal const int Capacity=24;
    private const float FootstepDistance=.72f,MaximumLifetime=.96f;
    private struct Leaf
    {
        internal float X,Y,Z,Vx,Vz,Born;
        internal uint Variation;
    }

    private Leaf[]? leaves;
    private VertexPositionColorTexture[]? vertices;
    private DynamicMesh? mesh;
    private readonly Material material=new Material{
        Lighting=LightingMode.Unlit,Blend=BlendMode.Alpha,Cull=CullMode.Back,
        DepthTest=true,DepthWrite=false,AlphaCutoff=1f/255
    };
    private readonly Transform identity=new Transform();
    private int newest,count,live,uploaded;
    private bool leftFoot;
    private float distance;
    private uint random=2166136261u;

    internal void TrackStep(Town town,float oldX,float oldZ,float x,float z,float seconds,bool running)
    {
        float dx=x-oldX,dz=z-oldZ,squared=dx*dx+dz*dz;
        if(squared==0)return;
        int ground=town.At((int)x,(int)z);
        if(ground!=(int)Terrain.Grass&&ground!=(int)Terrain.Camp){
            distance=0;
            return;
        }
        float step=(float)Math.Sqrt(squared);
        distance+=step;
        if(distance<FootstepDistance)return;
        // Sample along the actual step at the stride marker.
        distance-=FootstepDistance;
        dx/=step;dz/=step;
        float side=leftFoot?-.12f:.12f;
        leftFoot=!leftFoot;
        float footX=x-dx*(distance+.12f)+dz*side;
        float footZ=z-dz*(distance+.12f)-dx*side;
        float height=TerrainElevation.At(town,footX,footZ);
        var pool=leaves??=new Leaf[Capacity];
        int burst=running?4:3;
        for(int i=0;i<burst;i++){
            random=unchecked(random*1664525u+1013904223u);
            float fan=-.46f+i*.36f+Variation(random,8)*.15f;
            float back=.3f+Variation(random,16)*.36f;
            pool[newest]=new Leaf{
                X=footX+dz*i*.027f,Z=footZ-dx*i*.027f,Y=height,
                Vx=-dx*back+dz*fan+.32f,Vz=-dz*back-dx*fan,
                Born=seconds,Variation=random
            };
            newest=(newest+1)%Capacity;
            if(count<Capacity)count++;
        }
    }

    // The native mesh keeps its index count. Compact the visible quads at the
    // front and leave unused slots degenerate.
    internal int WriteVertices(VertexPositionColorTexture[] target,float focusZ,float seconds)
    {
        if(leaves==null)return 0;
        int oldest=(newest-count+Capacity)%Capacity,written=0;
        while(count>0&&seconds-leaves[oldest].Born>=MaximumLifetime){
            oldest=(oldest+1)%Capacity;
            count--;
        }
        for(int i=0;i<count;i++){
            var leaf=leaves[(oldest+i)%Capacity];
            float age=seconds-leaf.Born;
            float progress=age/(.64f+Variation(leaf.Variation,16)*.32f);
            if(progress>=1)continue;
            WriteLeaf(target,written*4,leaf,focusZ,age,progress);
            written++;
        }
        return written;
    }

    private static float Variation(uint seed,int shift)=>((seed>>shift)&255)*(1f/255);
    private static float Wave(float cycles)
    {
        float triangle=1-4*Math.Abs(cycles-(int)cycles-.5f);
        return triangle*(2-Math.Abs(triangle));
    }

    private static void WriteLeaf(VertexPositionColorTexture[] target,int at,Leaf leaf,float focusZ,float age,float progress)
    {
        uint seed=leaf.Variation;
        float phase=Variation(seed,24),arc=4*progress*(1-progress);
        float x=leaf.X+leaf.Vx*age+.12f*arc*Wave(age*1.6f+phase);
        float z=leaf.Z+leaf.Vz*age,height=leaf.Y+.065f+arc*(.18f+.27f*phase);
        var p=CylinderWorld.Surface(z,focusZ,height);
        // The outdoor camera's rise/back ratio is 3:4. Rotate in its plane,
        // narrowing the quad each time it rolls edge-on.
        float spin=phase+age*(.55f+phase*.8f);
        float half=.062f+.046f*Variation(seed,8);
        float sine=Wave(spin),cosine=Wave(spin+.25f);
        float width=half*(.16f+.84f*Math.Abs(Wave(phase+age*2.5f)));
        float rx=cosine*width,ru=sine*width,ux=-sine*half,uu=cosine*half;
        byte alpha=(byte)(245*Math.Min(1,(1-progress)*3));
        int palette=(int)((seed>>5)%3);
        Color32 color=palette==0?new Color32(231,109,47,alpha):palette==1?new Color32(252,199,78,alpha):new Color32(204,77,51,alpha);
        int cell=(int)((seed>>11)&1)*16;
        float u0=(cell+.5f)/32,u1=(cell+15.5f)/32;
        const float v0=.5f/16,v1=15.5f/16;
        // Clockwise in the camera plane, like the dropped-item billboards.
        target[at]=new VertexPositionColorTexture(new Vector3(x-rx-ux,p.Y-.8f*(uu+ru),p.Z+.6f*(uu+ru)),color,new Vector2(u0,v1));
        target[at+1]=new VertexPositionColorTexture(new Vector3(x-rx+ux,p.Y+.8f*(uu-ru),p.Z-.6f*(uu-ru)),color,new Vector2(u0,v0));
        target[at+2]=new VertexPositionColorTexture(new Vector3(x+rx+ux,p.Y+.8f*(ru+uu),p.Z-.6f*(ru+uu)),color,new Vector2(u1,v0));
        target[at+3]=new VertexPositionColorTexture(new Vector3(x+rx-ux,p.Y+.8f*(ru-uu),p.Z-.6f*(ru-uu)),color,new Vector2(u1,v1));
    }

    internal void Prepare(float focusZ,float seconds,int environmentTint)
    {
        live=0;
        if(count==0)return;
        var staging=vertices??=new VertexPositionColorTexture[Capacity*4];
        live=WriteVertices(staging,focusZ,seconds);
        if(live==0)return;
        material.Color=new Color32((byte)(environmentTint>>16),(byte)(environmentTint>>8),(byte)environmentTint);
        if(mesh==null){
            var indices=new ushort[Capacity*6];
            for(int i=0;i<Capacity;i++){
                int at=i*6,v=i*4;
                indices[at]=(ushort)v;indices[at+1]=(ushort)(v+1);indices[at+2]=(ushort)(v+2);
                indices[at+3]=(ushort)v;indices[at+4]=(ushort)(v+2);indices[at+5]=(ushort)(v+3);
            }
            material.Texture=FallLeafAtlas.Create();
            mesh=DynamicMesh.Create(staging,indices);
        }else{
            if(live<uploaded)Array.Clear(staging,live*4,(uploaded-live)*4);
            mesh.SetVertices(staging,count:Math.Max(live,uploaded)*4);
        }
        uploaded=live;
    }

    internal void Draw(Renderer renderer)
    {
        if(live>0&&mesh!=null)renderer.DrawMesh(mesh,material,identity);
    }
    internal void Clear()
    {
        newest=count=live=0;distance=0;leftFoot=false;
    }
    public void Dispose()
    {
        Clear();uploaded=0;
        mesh?.Dispose();mesh=null;
        material.Texture?.Dispose();material.Texture=null;
        leaves=null;vertices=null;
    }
}
