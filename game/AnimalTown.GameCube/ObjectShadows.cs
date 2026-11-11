using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Fixed caster cache. Clipped triangles feed a shared coverage union instead
// of independently blended native meshes.
internal sealed class ObjectShadows
{
    internal const int Capacity=32;
    private sealed class Entry
    {
        internal int Key=-1,Version,Published=-1;
        internal ShadowCaster Caster;
        internal bool Ready;
        internal readonly float[] Bounds=new float[4],Center=new float[2];
        internal int Count;
    }
    private readonly Entry[] entries=new Entry[Capacity];
    private readonly ShadowCaster[] casters=new ShadowCaster[512+BuildingWorld.Count];
    private readonly float[] radialY=new float[512+BuildingWorld.Count],radialZ=new float[512+BuildingWorld.Count];
    private readonly bool[] exists=new bool[512+BuildingWorld.Count];
    private readonly int[] selected=new int[Capacity];
    private readonly float[] distances=new float[Capacity];
    private readonly GroundShadow staging=new GroundShadow();
    private readonly ShadowReceiverCache receivers=new ShadowReceiverCache();
    private Town? town;
    private int groundVersion=-1,terrainVersion=-1,houseHash,building=-1;
    private float focusX=float.NaN,focusZ;
    internal int Triangles {get;private set;}
    internal bool Enabled=true;
    internal ObjectShadows()
    {
        for(int i=0;i<Capacity;i++)entries[i]=new Entry();
    }
    private static bool Same(ShadowCaster a,ShadowCaster b)=>a.X==b.X&&a.Z==b.Z&&a.Scale==b.Scale&&a.Profile==b.Profile&&a.Height==b.Height;
    private void SetCaster(int key,ShadowCaster value)
    {
        if(!Same(casters[key],value)){
            var p=CylinderWorld.Surface((value.Top+value.Bottom)*.5f,0,value.Height);
            radialY[key]=p.Y;radialZ[key]=p.Z;casters[key]=value;
        }
        exists[key]=true;
    }
    internal void Prepare(GameState state)
    {
        var t=state.Town;int hash=state.HousePlaced?1:0;
        for(int i=0;i<BuildingWorld.Count;i++){BuildingWorld.Origin(state,i,out float x,out float z);hash=unchecked(hash*31+(int)x*397+(int)z+(BuildingWorld.Exists(state,i)?1:0));}
        bool changed=t!=town||groundVersion!=t.ShadowVersion||terrainVersion!=t.TerrainVersion||hash!=houseHash;
        if(changed){
            if(t!=town){for(int i=0;i<Capacity;i++)entries[i].Key=-1;building=-1;}
            town=t;groundVersion=t.ShadowVersion;terrainVersion=t.TerrainVersion;houseHash=hash;
            Array.Clear(exists,0,exists.Length);
            for(int i=0;i<t.GroundCount;i++){
                var g=t.Ground[i];if(g.Kind!=3&&(g.Kind!=2||g.Item==2))continue;
                int profile=g.Item==2?1:g.Item==3?2:0;float scale=g.Kind==2?.75f:3;
                SetCaster(i,new ShadowCaster(g.X,g.Z,scale,profile,TerrainElevation.At(t,g.X,g.Z)));
            }
            for(int i=0;i<BuildingWorld.Count;i++)if(BuildingWorld.Exists(state,i)){
                BuildingWorld.Origin(state,i,out float x,out float z);x+=1.5f;z+=1.5f;
                int key=512+i;SetCaster(key,new ShadowCaster(x,z,3,3,TerrainElevation.At(t,x,z)));
            }
            for(int i=0;i<Capacity;i++){
                var e=entries[i];if(e.Key<0)continue;
                if(!exists[e.Key]||!Same(e.Caster,casters[e.Key])||e.Version!=terrainVersion&&!receivers.Valid(t,e.Caster)){e.Ready=false;if(building==i)building=-1;}else e.Version=terrainVersion;
            }
        }
        float fx=(float)Math.Floor(state.X*2)*.5f,fz=(float)Math.Floor(state.Z*2)*.5f;
        if(!changed&&focusX==fx&&focusZ==fz){Publish();return;}focusX=fx;focusZ=fz;
        int count=0;float eye=TerrainElevation.At(t,state.X,state.Z);var scroll=CylinderWorld.Scroll(0,state.Z);
        for(int key=0;key<exists.Length;key++)if(exists[key]){
            var c=casters[key];float cx=(c.Left+c.Right)*.5f,cz=(c.Top+c.Bottom)*.5f;
            if(Math.Abs(cx-fx)>CylinderWorld.ViewHalfWidth+5||cz<fz-TerrainTiles.GroundRowsAhead-5||cz>fz+TerrainTiles.GroundRowsBehind+5)continue;
            float py=scroll.Cos*radialY[key]-scroll.Sin*radialZ[key]+scroll.Y,pz=scroll.Sin*radialY[key]+scroll.Cos*radialZ[key]+scroll.Z;
            float radius=(c.Right-c.Left+c.Bottom-c.Top)*.5f;
            if(!RenderView.Sphere(cx,py,pz,radius,state.X,state.Z,eye)||!RenderView.AboveHorizon(py,pz,radius,state.Z,eye))continue;
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
        Publish();
    }
    private void Publish(){for(int i=0;i<Capacity;i++){
        var e=entries[i];int active=Enabled&&e.Key>=0&&e.Ready&&e.Count>0?1:0;
        if(e.Published!=active){NativeShadows.Active(i,active,e.Center);e.Published=active;}
    }}
    // Called only while the terrain renderer's SAME deadline has time left.
    internal bool Advance()
    {
        if(town==null||!Enabled)return false;
        if(building<0){
            for(int i=0;i<Capacity;i++)if(entries[i].Key>=0&&!entries[i].Ready){building=i;break;}
            if(building<0)return false;
            var next=entries[building];next.Caster=casters[next.Key];next.Version=terrainVersion;
            if(receivers.Restore(town,next.Caster,staging.Uvs,out int count,out bool flat)){Complete(count);return true;}
            staging.BeginCoverage(town,next.Caster);
        }
        if(!staging.Advance())return true;
        int vertices=staging.Overflow?0:staging.Vertices;
        receivers.Store(town,entries[building].Caster,staging.Uvs,vertices,staging.FlatCoverage);Complete(vertices);return true;
    }
    private void Complete(int count)
    {
        var e=entries[building];e.Count=count;
        e.Bounds[0]=e.Caster.Left;e.Bounds[1]=e.Caster.Top;e.Bounds[2]=e.Caster.Right;e.Bounds[3]=e.Caster.Bottom;
        if(NativeShadows.Mesh(building,e.Count,e.Caster.Profile,staging.Uvs,e.Bounds)!=1)throw new InvalidOperationException("Invalid scenery shadow coverage");
        NativeShadows.Active(building,e.Count>0?1:0,e.Center);
        e.Published=e.Count>0?1:0;e.Ready=true;building=-1;
    }
}
