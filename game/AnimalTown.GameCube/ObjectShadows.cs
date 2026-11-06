using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Fixed caster cache: 32 native handles, shared atlas/material, no frame-time
// vertex rebuilding. Offscreen casters are selected by their shadow bounds.
internal sealed class ObjectShadows
{
    internal const int Capacity=32;
    private sealed class Entry
    {
        internal int Key=-1,Version;
        internal ShadowCaster Caster;
        internal bool Ready;
        internal DynamicMeshBuffer? Buffer;
        internal long Revision;
        internal int Count;
    }
    private readonly Entry[] entries=new Entry[Capacity];
    private readonly ShadowCaster[] casters=new ShadowCaster[512+BuildingWorld.Count];
    private readonly bool[] exists=new bool[512+BuildingWorld.Count];
    private readonly int[] selected=new int[Capacity];
    private readonly float[] distances=new float[Capacity];
    private readonly GroundShadow staging=new GroundShadow();
    private readonly Mesh[] meshes=new Mesh[Capacity];
    private readonly Transform[] poses=new Transform[Capacity];
    private readonly Texture2D atlas;
    private readonly Material material;
    private Town? town;
    private int groundVersion=-1,terrainVersion=-1,houseHash,building=-1;
    private float focusX=float.NaN,focusZ;
    internal int Triangles {get;private set;}
    internal bool Enabled=true;
    internal ObjectShadows(Transform scroll)
    {
        atlas=Texture2D.CreateNative(128,128,NativeTextureFormat.Ia8,true);atlas.ReplaceNative(GroundShadow.Atlas());
        material=new Material{Texture=atlas,Color=new Color32(35,49,66,66),Blend=BlendMode.Alpha,DepthWrite=false,Lighting=LightingMode.Unlit,Cull=CullMode.Back,Sampler=new SamplerState(TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureFilter.Linear)};
        for(int i=0;i<Capacity;i++){entries[i]=new Entry();poses[i]=scroll;}
    }
    private static bool Same(ShadowCaster a,ShadowCaster b)=>a.X==b.X&&a.Z==b.Z&&a.Scale==b.Scale&&a.Profile==b.Profile&&a.Height==b.Height;
    internal void Prepare(GameState state)
    {
        var t=state.Town;int hash=state.HousePlaced?1:0;
        for(int i=0;i<BuildingWorld.Count;i++){BuildingWorld.Origin(state,i,out float x,out float z);hash=unchecked(hash*31+(int)x*397+(int)z);}
        bool changed=t!=town||groundVersion!=t.ShadowVersion||terrainVersion!=t.TerrainVersion||hash!=houseHash;
        if(changed){
            if(t!=town){for(int i=0;i<Capacity;i++)entries[i].Key=-1;building=-1;}
            town=t;groundVersion=t.ShadowVersion;terrainVersion=t.TerrainVersion;houseHash=hash;
            Array.Clear(exists,0,exists.Length);
            for(int i=0;i<t.GroundCount;i++){
                var g=t.Ground[i];if(g.Kind!=3&&(g.Kind!=2||g.Item==2))continue;
                int profile=g.Item==2?1:g.Item==3?2:0;float scale=g.Kind==2?.75f:3;
                casters[i]=new ShadowCaster(g.X,g.Z,scale,profile,TerrainElevation.At(t,g.X,g.Z));exists[i]=true;
            }
            for(int i=0;i<BuildingWorld.Count;i++)if(BuildingWorld.Exists(state,i)){
                BuildingWorld.Origin(state,i,out float x,out float z);x+=1.5f;z+=1.5f;
                int key=512+i;casters[key]=new ShadowCaster(x,z,3,3,TerrainElevation.At(t,x,z));exists[key]=true;
            }
            for(int i=0;i<Capacity;i++){
                var e=entries[i];if(e.Key<0)continue;
                if(!exists[e.Key]||!Same(e.Caster,casters[e.Key])||e.Version!=terrainVersion){e.Ready=false;if(building==i)building=-1;}
            }
        }
        float fx=(float)Math.Floor(state.X*2)*.5f,fz=(float)Math.Floor(state.Z*2)*.5f;
        if(!changed&&focusX==fx&&focusZ==fz)return;focusX=fx;focusZ=fz;
        int count=0;
        for(int key=0;key<exists.Length;key++)if(exists[key]){
            var c=casters[key];float cx=(c.Left+c.Right)*.5f,cz=(c.Top+c.Bottom)*.5f;
            if(Math.Abs(cx-fx)>CylinderWorld.ViewHalfWidth+5||cz<fz-TerrainTiles.GroundRowsAhead-5||cz>fz+TerrainTiles.GroundRowsBehind+5)continue;
            var p=CylinderWorld.Surface(cz,state.Z,c.Height);float radius=(c.Right-c.Left+c.Bottom-c.Top)*.5f;
            if(!RenderView.Sphere(cx,p.Y,p.Z,radius,state.X,state.Z,TerrainElevation.At(t,state.X,state.Z))||!RenderView.AboveHorizon(p.Y,p.Z,radius,state.Z,TerrainElevation.At(t,state.X,state.Z)))continue;
            float d=(cx-fx)*(cx-fx)+(cz-fz)*(cz-fz)-(c.Profile==3?10000:0);int at=count;
            if(at==Capacity){if(d>=distances[Capacity-1])continue;at--;}
            else count++;
            while(at>0&&d<distances[at-1]){if(at<Capacity){selected[at]=selected[at-1];distances[at]=distances[at-1];}at--;}
            selected[at]=key;distances[at]=d;
        }
        for(int i=0;i<Capacity;i++){
            var e=entries[i];bool wanted=false;for(int n=0;n<count;n++)if(e.Key==selected[n]){wanted=true;break;}
            if(!wanted){e.Key=-1;e.Ready=false;if(building==i)building=-1;}
        }
        for(int n=0;n<count;n++){
            int key=selected[n];bool found=false;for(int i=0;i<Capacity;i++)if(entries[i].Key==key){found=true;break;}
            if(found)continue;
            for(int i=0;i<Capacity;i++)if(entries[i].Key<0){entries[i].Key=key;entries[i].Ready=false;break;}
        }
    }
    // Called only while the terrain renderer's SAME deadline has time left.
    internal bool Advance()
    {
        if(town==null||!Enabled)return false;
        if(building<0){
            for(int i=0;i<Capacity;i++)if(entries[i].Key>=0&&!entries[i].Ready){building=i;break;}
            if(building<0)return false;
            var next=entries[building];next.Caster=casters[next.Key];next.Version=terrainVersion;staging.Begin(town,next.Caster);
        }
        if(!staging.Advance())return true;
        var e=entries[building];e.Count=staging.Overflow?0:staging.Vertices;
        if(e.Count>0){
            if(e.Buffer==null)e.Buffer=new DynamicMeshBuffer(GroundShadow.MaximumTriangles*3);
            var buffer=e.Buffer.Reserve(e.Count,FrameProfile.Counters);
            for(int i=0;i<e.Count;i++)buffer[i]=new VertexPositionColorTexture(new Vector3(staging.Positions[i*3],staging.Positions[i*3+1],staging.Positions[i*3+2]),Color32.White,new Vector2(staging.Uvs[i*2],staging.Uvs[i*2+1]));
            e.Buffer.Commit(e.Count,++e.Revision,FrameProfile.Counters);
        }
        e.Ready=true;building=-1;return true;
    }
    internal void Draw(Renderer renderer)
    {
        int count=0;Triangles=0;if(!Enabled)return;
        for(int i=0;i<Capacity;i++){var e=entries[i];if(e.Key>=0&&e.Ready&&e.Count>0){meshes[count++]=e.Buffer!.Mesh!;Triangles+=e.Count/3;}}
        if(count>0)renderer.DrawMeshes(meshes,poses,material,0,count);
    }
}
