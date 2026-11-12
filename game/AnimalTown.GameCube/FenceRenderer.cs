using System;
using AnimalTown;
using Dolphin.Graphics;

// Four immutable sides, baked into the same reference cylinder as terrain.
// Each side retains the original per-post visibility envelope. Travel changes
// only the shared scroll transform; garden/terrain edits rebuild between frames.
internal sealed class FenceRenderer : IDisposable
{
    private readonly Mesh?[] meshes=new Mesh?[16];
    private readonly float[] centerX=new float[224],centerY=new float[224],centerZ=new float[224];
    private readonly int[] starts=new int[17];
    private readonly Material material=new Material{Color=new Color32(211,191,147),Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private Town? town;
    private int version=-1,tier=-1;
    private bool placed;
    private const float Radius=.3378f;
    public void Dispose()
    {
        for(int i=0;i<meshes.Length;i++){meshes[i]?.Dispose();meshes[i]=null;}
        town=null;version=tier=-1;placed=false;
    }
    internal void Prepare(GameState state)
    {
        int stamp=0;for(int p=0;p<GameState.ProfileCount;p++)stamp=unchecked(stamp*31+state.HomeX(p)*397+state.HomeZ(p)*17+state.HomeGardenTier(p)*2+(state.HomePlaced(p)?1:0));
        if(town==state.Town&&version==state.Town.TerrainVersion&&tier==stamp)return;
        for(int i=0;i<16;i++){meshes[i]?.Dispose();meshes[i]=null;}
        town=state.Town;version=town.TerrainVersion;tier=stamp;placed=false;int count=0;
        float[] cube={0,0,0,1,0,0,1,1,0,0,1,0,0,0,1,1,0,1,1,1,1,0,1,1};
        int[] ids={0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4};
        for(int profile=0;profile<GameState.ProfileCount;profile++)for(int side=0;side<4;side++){
            int group=profile*4+side;starts[group]=count;if(!state.HomePlaced(profile))continue;placed=true;
            int x=state.HomeX(profile),z=state.HomeZ(profile),w=GameState.GardenWidths[state.HomeGardenTier(profile)],h=GameState.GardenHeights[state.HomeGardenTier(profile)];int posts=side==0?w:side==1?w-1:h-2;
            var vertices=new VertexPositionColor[posts*8];var indices=new ushort[posts*36];
            for(int j=0;j<posts;j++){
                int px=x+(side<2?(side==1&&j>=3?j+1:j):side==2?0:w-1),pz=z+(side==0?0:side==1?h-1:j+1);
                var p=CylinderWorld.Surface(pz,0,TerrainElevation.At(town,px,pz));
                centerX[count]=px+.06f;centerY[count]=p.Y+.275f*p.UpY-.06f*p.UpZ;centerZ[count++]=p.Z+.275f*p.UpZ+.06f*p.UpY;
                for(int i=0;i<8;i++){
                    int at=i*3;float cy=cube[at+1]*.55f,cz=cube[at+2]*.12f;
                    vertices[j*8+i]=new VertexPositionColor(new Vector3(px+cube[at]*.12f,p.Y+cy*p.UpY-cz*p.UpZ,p.Z+cy*p.UpZ+cz*p.UpY),Color32.White);
                }
                for(int i=0;i<36;i++)indices[j*36+i]=(ushort)(j*8+ids[i%3==0?i:i%3==1?i+1:i-1]);
            }
            meshes[group]=Mesh.Create(vertices,indices);
        }
        starts[16]=count;
    }
    internal void Draw(Renderer renderer,GameState state,Transform scroll,float eyeHeight)
    {
        if(!placed)return;var motion=CylinderWorld.Scroll(0,state.Z);
        for(int side=0;side<16;side++){
            if(meshes[side]==null)continue;bool visible=false;
            for(int i=starts[side];i<starts[side+1];i++){
                float y=motion.Cos*centerY[i]-motion.Sin*centerZ[i]+motion.Y,z=motion.Sin*centerY[i]+motion.Cos*centerZ[i]+motion.Z;
                if(RenderView.Sphere(centerX[i],y,z,Radius,state.X,state.Z,eyeHeight)&&RenderView.AboveHorizon(y,z,Radius,state.Z,eyeHeight))visible=true;else FrameProfile.CulledObjects++;
            }
            if(visible)renderer.DrawMesh(meshes[side]!,material,scroll);
        }
    }
}
