using System;
using AnimalTown;
using Dolphin.Graphics;

// Exteriors/doors stay resident. Shared interior parts are restored from verified
// ARAM backing before player entry, and released after their last visible use.
internal sealed class BuildingRenderer
{
    private readonly Model?[] models=new Model?[BuildingCatalog.MaximumDefinitions*7];
    private readonly ModelInstance?[] parts=new ModelInstance?[BuildingCatalog.MaximumDefinitions*7];
    private readonly Transform[] roots=new Transform[BuildingWorld.Count],poses=new Transform[32];
    private readonly bool[] ready=new bool[BuildingCatalog.MaximumDefinitions];
    private readonly AssetResidency residency;
    private int used;
    private readonly StaticCylinderPlacements placements=new StaticCylinderPlacements(BuildingWorld.Count);
    private readonly Transform scrollRoot;
    private readonly RenderQueue staticQueue=new RenderQueue(128,128);
    private bool cachedPlacement;
    internal int ResidentBytes {get;private set;}
    internal BuildingRenderer(Transform world,AssetResidency residency)
    {
        this.residency=residency;scrollRoot=new Transform{Parent=world};
        for(int i=0;i<roots.Length;i++)roots[i]=new Transform{Parent=scrollRoot};for(int i=0;i<poses.Length;i++)poses[i]=new Transform{Parent=world};
    }
    private System.Threading.Tasks.Task<Model>? pending;
    private int loadType,loadPart,typeStartBytes;
    private string pendingPath="";
    internal int LoadedParts=>loadType*7+loadPart;
    internal string LoadingLabel {get;private set;}="Loading buildings";
    internal bool AdvanceLoading()
    {
        if(loadType>=BuildingCatalog.Count)return true;
        var d=BuildingCatalog.Get(loadType);
        try{
            if(pending==null){
                string name=loadPart==0?d.Exterior:loadPart==1?d.Door:loadPart==2?d.Floor:loadPart==3?d.North:loadPart==4?d.East:loadPart==5?d.South:d.West;
                LoadingLabel="Loading "+name;pendingPath=TextureStreaming.ModelPath(name,TextureResolution.Size128);pending=Model.LoadAsync(pendingPath,default,256*1024);return false;
            }
            if(!pending.IsCompleted)return false;
            int slot=loadType*7+loadPart;models[slot]=pending.GetAwaiter().GetResult();residency.Register(AssetDemand.BuildingPart(loadType,loadPart),models[slot]!,pendingPath,loadPart<2);parts[slot]=models[slot]!.CreateInstance(16*1024);
            ResidentBytes+=models[slot]!.NativeBytes+parts[slot]!.NativeBytes;
            if(ResidentBytes>768*1024)throw new InvalidOperationException("Building resident memory budget");
            loadPart++;if(loadPart==7)ready[loadType]=true;
        }catch(Exception error){
            for(int i=0;i<7;i++){int slot=loadType*7+i;residency.Forget(AssetDemand.BuildingPart(loadType,i));parts[slot]?.Dispose();parts[slot]=null;models[slot]?.Dispose();models[slot]=null;}
            ResidentBytes=typeStartBytes;loadPart=7;Dolphin.Diagnostics.DebugOverlay.WriteLine("BUILDING ASSET ERROR "+d.Id+": "+error.Message);
        }
        pending=null;if(loadPart==7){loadType++;loadPart=0;typeStartBytes=ResidentBytes;}
        return loadType>=BuildingCatalog.Count;
    }
    internal void Begin(PortalSystem doors,GameState state)
    {
        used=0;staticQueue.Clear();cachedPlacement=TextureStreaming.Enabled(16);placements.Begin(state.Town,state.Town.TerrainVersion,state.Z);var scroll=placements.Scroll;
        scrollRoot.LocalPosition=cachedPlacement?new Vector3(0,scroll.Y,scroll.Z):Vector3.Zero;
        scrollRoot.LocalRotation=cachedPlacement?new Quaternion(scroll.HalfSin,0,0,scroll.HalfCos):new Quaternion(0,0,0,1);
        for(int i=0;i<ready.Length;i++)doors.Ready[i]=ready[i];
    }
    internal void Flush(Renderer renderer){if(staticQueue.Count>0)staticQueue.Execute(renderer);}
    internal bool Ready(int type)=>type>=0&&type<ready.Length&&ready[type];
    private void Part(Renderer renderer,int type,int part,Transform parent,Vector3 position,Vector3 scale,float yaw=0)
    {
        if(!Ready(type)||!residency.Ready(AssetDemand.BuildingPart(type,part)))return;if(used==poses.Length)throw new InvalidOperationException("Building draw budget");var pose=poses[used++];pose.Parent=parent;pose.LocalPosition=position;pose.LocalScale=scale;pose.LocalRotation=new Quaternion(0,(float)Math.Sin(yaw*.5f),0,(float)Math.Cos(yaw*.5f));TextureStreaming.Request(AssetDemand.BuildingPart(type,part),128);if(staticQueue.Count==staticQueue.Capacity)Flush(renderer);staticQueue.Submit(parts[type*7+part]!,pose);
    }
    internal void Exterior(Renderer renderer,GameState state,int building,PortalSystem doors)
    {
        if(!BuildingWorld.Exists(state,building))return;int type=state.BuildingTypes[building];if(!Ready(type))return;
        BuildingWorld.Origin(state,building,out float x,out float z);float h=TerrainElevation.At(state.Town,x+1.5f,z+1.5f);var cached=placements.Get(building,(int)x,(int)z,h);var p=cachedPlacement?placements.Project(cached):CylinderWorld.Surface(z,state.Z,h);
        if(!RenderView.Sphere(x+1.5f,p.Y+1.6f,p.Z+1.5f,3.5f,state.X,state.Z,TerrainElevation.At(state.Town,state.X,state.Z)))return;
        var root=roots[building];root.LocalPosition=new Vector3(x,cachedPlacement?cached.Surface.Y:p.Y,cachedPlacement?cached.Surface.Z:p.Z);float a=(z-state.Z)/CylinderWorld.Radius*.5f;root.LocalRotation=cachedPlacement?new Quaternion(cached.HalfSin,0,0,cached.HalfCos):new Quaternion((float)Math.Sin(a),0,0,(float)Math.Cos(a));root.LocalScale=new Vector3(1,1,1);
        Part(renderer,type,0,root,Vector3.Zero,new Vector3(1,1,1));Part(renderer,type,1,root,new Vector3(BuildingWorld.Definition(state,building).HingeX,0,BuildingWorld.Definition(state,building).HingeZ),new Vector3(1,1,1),-doors.Openness(building)*1.4f);
    }
    internal void Interior(Renderer renderer,GameState state,Transform world,InteriorCamera camera,PortalSystem doors)
    {
        int building=BuildingWorld.Building(state.Space),type=state.BuildingTypes[building];float size=BuildingWorld.Size(state,state.Space),scale=size/4;
        NativeShadows.Scope(2);
        try{Part(renderer,type,2,world,Vector3.Zero,new Vector3(scale,1,scale));Flush(renderer);}finally{NativeShadows.Scope(0);}
        for(int wall=0;wall<4;wall++)if(camera.WallVisible(wall))Part(renderer,type,3+wall,world,Vector3.Zero,new Vector3(scale,1,scale));
        // Keep the doorway legible even when the front wall is cut away.
        if(camera.WallVisible(2)||doors.Openness(building)>0)Part(renderer,type,1,world,new Vector3(size*.5f-.55f*scale,0,size+.01f),new Vector3(scale,1,1),doors.Openness(building)*1.4f);
    }
}
