using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;

// Exteriors/doors share a compact permanent kit. Floors/walls follow the player
// and both sides of their doorway; invisible NPC rooms never create demand.
internal sealed class BuildingRenderer : IDisposable
{
    private const int PartsPerDefinition=7;
    private readonly Model?[] models=new Model?[BuildingCatalog.MaximumDefinitions*PartsPerDefinition];
    private readonly ModelInstance?[] parts=new ModelInstance?[BuildingCatalog.MaximumDefinitions*PartsPerDefinition];
    private readonly Transform[] roots=new Transform[BuildingWorld.Count],poses=new Transform[32];
    private readonly bool[] ready=new bool[BuildingCatalog.MaximumDefinitions];
    private readonly bool[] required=new bool[BuildingCatalog.MaximumDefinitions],wanted=new bool[BuildingCatalog.MaximumDefinitions];
    private readonly long[] lastRequired=new long[BuildingCatalog.MaximumDefinitions],retryAt=new long[BuildingCatalog.MaximumDefinitions];
    private readonly int[] ownedBytes=new int[BuildingCatalog.MaximumDefinitions*PartsPerDefinition];
    private const long IdleGraceUs=3000000,RetryDelayUs=1000000;
    private const int ModelBudgetBytes=256*1024,PartPeakBytes=ModelBudgetBytes+16*1024;
    private int residencySlot=-1;
    private int memoryTicket;
    private int pendingFailureCount;
    private readonly int[] failureSnapshot=new int[5];
    internal VisualMemoryPressure? Memory;
    private int used;
    internal int ResidentBytes {get;private set;}
    internal BuildingRenderer(Transform world)
    {
        for(int i=0;i<roots.Length;i++)roots[i]=new Transform{Parent=world};for(int i=0;i<poses.Length;i++)poses[i]=new Transform{Parent=world};
    }
    private System.Threading.Tasks.Task<Model>? pending;
    private int loadType,loadPart,typeStartBytes;
    internal int LoadedParts=>loadType*PartsPerDefinition+loadPart;
    internal string LoadingLabel {get;private set;}="Loading buildings";
    public void Dispose()
    {
        // A completed async load owns a native model even before AdvanceLoading
        // adopts it. Drain it outside a frame so disc work cannot outlive a draft.
        Model? unadopted=null;
        if(pending!=null){try{unadopted=pending.GetAwaiter().GetResult();}catch(Exception){if(!pending.IsCompleted)throw;}pending=null;}
        for(int i=0;i<parts.Length;i++){parts[i]?.Dispose();parts[i]=null;}
        unadopted?.Dispose();
        for(int i=0;i<models.Length;i++){models[i]?.Dispose();models[i]=null;}
        Memory?.Cancel(memoryTicket);memoryTicket=0;
        Array.Clear(ready,0,ready.Length);Array.Clear(ownedBytes,0,ownedBytes.Length);ResidentBytes=0;loadType=BuildingCatalog.Count;loadPart=used=0;residencySlot=-1;
    }
    internal bool AdvanceLoading(GameState? state=null)
    {
        if(loadType>=BuildingCatalog.Count)return true;
        if(Clock.Timestamp<retryAt[loadType])return false;
        var d=BuildingCatalog.Get(loadType);
        try{
            if(pending==null){
                int indoor=state==null?-1:BuildingWorld.Building(state.Space);
                if(state!=null&&loadPart==2&&(indoor<0||state.BuildingTypes[indoor]!=loadType)){
                    ready[loadType]=true;loadType++;loadPart=0;typeStartBytes=ResidentBytes;return loadType>=BuildingCatalog.Count;
                }
                string name=PartName(d,loadPart);
                LoadingLabel="Loading "+name;
                if(Memory!=null&&!Memory.TryReserve(PartPeakBytes,Clock.Timestamp,true,out memoryTicket))return false;
                pendingFailureCount=Memory?.FailureCount??0;
                pending=Model.LoadAsync(TexturePolicy.ModelPath(name,TextureResolution.Size128),default,ModelBudgetBytes);return false;
            }
            if(!pending.IsCompleted)return false;
            int slot=loadType*PartsPerDefinition+loadPart;models[slot]=pending.GetAwaiter().GetResult();
            if(NativeBuildings.Retain(models[slot]!)!=1)throw new InvalidOperationException("Static building geometry retention failed");
            parts[slot]=models[slot]!.CreateInstance(16*1024);
            ownedBytes[slot]=models[slot]!.NativeBytes+parts[slot]!.NativeBytes;ResidentBytes+=ownedBytes[slot];
            if(ResidentBytes>768*1024)throw new InvalidOperationException("Building resident memory budget");
            Memory?.Complete(memoryTicket);memoryTicket=0;
            loadPart++;if(loadPart==PartsPerDefinition)ready[loadType]=true;
        }catch(Exception error){
            for(int i=0;i<PartsPerDefinition;i++)ReleasePart(loadType*PartsPerDefinition+i);
            ResidentBytes=typeStartBytes;
            Memory?.Cancel(memoryTicket);memoryTicket=0;
            // The SDK reports an invalid package and allocation exhaustion using
            // the same IOException. Retry startup only with allocator evidence;
            // a malformed/missing kit retains its normal unavailable-art path.
            bool allocationFailure=AllocationEvidence(error,Clock.Timestamp);
            if(allocationFailure){loadPart=0;retryAt[loadType]=Clock.Timestamp+RetryDelayUs;Memory?.AllocationFailed(PartPeakBytes,Clock.Timestamp);}
            else loadPart=PartsPerDefinition;
            Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN BUILDING ASSET "+(allocationFailure?"RETRY ":"ERROR ")+d.Id+": "+error.Message);
        }
        pending=null;if(loadPart==PartsPerDefinition){loadType++;loadPart=0;typeStartBytes=ResidentBytes;}
        return loadType>=BuildingCatalog.Count;
    }
    private static string PartName(BuildingDefinition d,int part)
        =>part==0?d.Exterior:part==1?d.Door:part==2?d.Floor:part==3?d.North:part==4?d.East:part==5?d.South:d.West;
    private bool AllocationEvidence(Exception error,long now)
    {
        bool failed=error is OutOfMemoryException;
        if(Memory!=null&&NativeMemory.Read(failureSnapshot)==1){
            failed|=failureSnapshot[3]!=pendingFailureCount;
            Memory.Observe(failureSnapshot[0],failureSnapshot[3],failureSnapshot[4],now);
        }
        return failed;
    }
    private void ReleasePart(int slot)
    {
        parts[slot]?.Dispose();parts[slot]=null;models[slot]?.Dispose();models[slot]=null;
        ResidentBytes-=ownedBytes[slot];ownedBytes[slot]=0;
    }
    private void DemandSpace(GameState state,int space)
    {
        int building=BuildingWorld.Building(space);if(building<0||!BuildingWorld.Exists(state,building))return;
        int type=state.BuildingTypes[building];if(type>=0&&type<required.Length)required[type]=wanted[type]=true;
    }
    private void Demand(GameState state,PortalSystem doors,long now,bool pressure)
    {
        Array.Clear(required,0,required.Length);Array.Clear(wanted,0,wanted.Length);
        DemandSpace(state,state.Space);
        if(doors.Busy(0)){DemandSpace(state,doors.Actors[0].Source.Space.Value);DemandSpace(state,doors.Actors[0].Destination.Space.Value);}
        // Eight metres gives disc restoration time before A can request entry.
        // Optional approach loads yield to global pressure and do not pin rooms.
        if(state.Space==0&&!pressure)for(int building=0;building<BuildingWorld.Count;building++)if(BuildingWorld.Exists(state,building)){
            BuildingWorld.Entrance(state,building,out float x,out float z);float dx=x-state.X,dz=z-state.Z;
            int type=state.BuildingTypes[building];if(dx*dx+dz*dz<=64&&type>=0&&type<wanted.Length)wanted[type]=true;
        }
        for(int type=0;type<wanted.Length;type++)if(wanted[type])lastRequired[type]=now;
    }
    internal bool ReadyForSpace(GameState state,int space)
    {
        int building=BuildingWorld.Building(space);if(building<0)return space==0;
        int type=state.BuildingTypes[building];if(!Ready(type))return false;
        for(int part=2;part<PartsPerDefinition;part++)if(parts[type*PartsPerDefinition+part]==null)return false;
        return true;
    }
    internal bool RequiredWorkPending
    {
        get{
            for(int type=0;type<required.Length;type++)if(required[type])
                for(int part=2;part<PartsPerDefinition;part++)if(parts[type*PartsPerDefinition+part]==null)return true;
            return false;
        }
    }
    internal bool ReleaseUnused(GameState state,PortalSystem doors,long now,bool pressure)
    {
        Demand(state,doors,now,pressure);
        for(int type=0;type<ready.Length;type++)if(!required[type]&&(pressure||!wanted[type]&&now-lastRequired[type]>=IdleGraceUs))
            for(int part=PartsPerDefinition-1;part>=2;part--)if(parts[type*PartsPerDefinition+part]!=null){ReleasePart(type*PartsPerDefinition+part);return true;}
        return false;
    }
    internal void PrepareResidency(GameState state,PortalSystem doors,long now,long deadline,bool pressure,bool allowOptional=true)
    {
        if(loadType<BuildingCatalog.Count)return;
        Demand(state,doors,now,pressure);
        if(pending!=null){
            if(!pending.IsCompleted)return;
            // Inspect one completion even with no CPU credit. A no-longer-wanted
            // optional result may be disposed, but adoption/native allocation
            // waits for its proper priority and a new bounded work grant.
            int candidateType=residencySlot/PartsPerDefinition;
            if(wanted[candidateType]&&(Clock.Timestamp>=deadline||!required[candidateType]&&!allowOptional))return;
            int slot=residencySlot,type=slot/PartsPerDefinition;residencySlot=-1;
            Model? loaded=null;
            try{
                loaded=pending.GetAwaiter().GetResult();pending=null;
                if(!wanted[type]){loaded.Dispose();Memory?.Cancel(memoryTicket);memoryTicket=0;return;}
                if(NativeBuildings.Retain(loaded)!=1)throw new InvalidOperationException("Static building geometry retention failed");
                models[slot]=loaded;loaded=null;parts[slot]=models[slot]!.CreateInstance(16*1024);
                ownedBytes[slot]=models[slot]!.NativeBytes+parts[slot]!.NativeBytes;ResidentBytes+=ownedBytes[slot];
                if(ResidentBytes>768*1024)throw new InvalidOperationException("Building resident memory budget");
                Memory?.Complete(memoryTicket);memoryTicket=0;
            }catch(Exception error){
                pending=null;loaded?.Dispose();ReleasePart(slot);retryAt[type]=now+RetryDelayUs;
                Memory?.Cancel(memoryTicket);memoryTicket=0;
                if(AllocationEvidence(error,now))Memory?.AllocationFailed(PartPeakBytes,now);
                Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN BUILDING RESIDENCY RETRY "+type+": "+error.Message);
            }
            return;
        }
        if(Clock.Timestamp>=deadline)return;
        for(int rank=0;rank<(allowOptional?2:1);rank++)for(int type=0;type<ready.Length;type++)if(ready[type]&&wanted[type]&&required[type]==(rank==0)&&now>=retryAt[type]){
            for(int part=2;part<PartsPerDefinition;part++)if(parts[type*PartsPerDefinition+part]==null){
                if(Memory!=null&&!Memory.TryReserve(PartPeakBytes,now,required[type],out memoryTicket))return;
                try{
                    pendingFailureCount=Memory?.FailureCount??0;
                    pending=Model.LoadAsync(TexturePolicy.ModelPath(PartName(BuildingCatalog.Get(type),part),TextureResolution.Size128),default,ModelBudgetBytes);
                    residencySlot=type*PartsPerDefinition+part;
                }catch(Exception error){Memory?.Cancel(memoryTicket);memoryTicket=0;if(AllocationEvidence(error,now))Memory?.AllocationFailed(PartPeakBytes,now);retryAt[type]=now+RetryDelayUs;Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN BUILDING RESIDENCY ADMISSION "+type+": "+error.Message);}
                return;
            }
        }
    }
    internal void Begin(PortalSystem doors){used=0;for(int i=0;i<ready.Length;i++)doors.Ready[i]=ready[i];}
    internal bool Ready(int type)=>type>=0&&type<ready.Length&&ready[type];
    private void Part(Renderer renderer,int type,int part,Transform parent,Vector3 position,Vector3 scale,float yaw=0)
    {
        if(!Ready(type)||parts[type*PartsPerDefinition+part]==null)return;if(used==poses.Length)throw new InvalidOperationException("Building draw budget");var pose=poses[used++];pose.Parent=parent;pose.LocalPosition=position;pose.LocalScale=scale;pose.LocalRotation=new Quaternion(0,(float)Math.Sin(yaw*.5f),0,(float)Math.Cos(yaw*.5f));renderer.DrawModel(parts[type*PartsPerDefinition+part]!,pose);
    }
    private void ExteriorParts(Renderer renderer,GameState state,int building,Transform root,float openness)
    {
        // Closing reuses the same exterior and hinged door as opening. Keeping
        // a third combined copy resident costs scarce MEM1 for one saved draw.
        int type=state.BuildingTypes[building];var definition=BuildingWorld.Definition(state,building);
        Part(renderer,type,0,root,Vector3.Zero,new Vector3(1,1,1));
        Part(renderer,type,1,root,new Vector3(definition.HingeX,0,definition.HingeZ),new Vector3(1,1,1),-openness*1.4f);
    }
    internal void Exterior(Renderer renderer,GameState state,int building,PortalSystem doors)
    {
        if(!BuildingWorld.Exists(state,building))return;int type=state.BuildingTypes[building];if(!Ready(type))return;
        BuildingWorld.Origin(state,building,out float x,out float z);float h=TerrainElevation.At(state.Town,x+1.5f,z+1.5f);var p=CylinderWorld.Surface(z,state.Z,h);
        float eye=TerrainElevation.At(state.Town,state.X,state.Z);
        if(!RenderView.Sphere(x+1.5f,p.Y+1.6f,p.Z+1.5f,3.5f,state.X,state.Z,eye)||
           !RenderView.AboveHorizon(p.Y+1.6f,p.Z+1.5f,3.5f,state.Z,eye))return;
        var root=roots[building];root.LocalPosition=new Vector3(x,p.Y,p.Z);float a=(z-state.Z)/CylinderWorld.Radius*.5f;root.LocalRotation=new Quaternion((float)Math.Sin(a),0,0,(float)Math.Cos(a));root.LocalScale=new Vector3(1,1,1);
        ExteriorParts(renderer,state,building,root,doors.Openness(building));
    }
    // Uses the already resident cottage and door. The ghost never places a
    // building, changes collision or allocates a second model/texture set.
    internal void PreviewHome(Renderer renderer,GameState state,int plotX,int plotZ)
    {
        int building=BuildingWorld.PlayerBuilding(state.ActiveProfile),type=state.BuildingTypes[building];
        if(!Ready(type))return;float x=plotX+2,z=plotZ+1;
        var p=CylinderWorld.Surface(z,state.Z,TerrainElevation.At(state.Town,x+1.5f,z+1.5f));
        float eye=TerrainElevation.At(state.Town,state.X,state.Z);
        if(!RenderView.Sphere(x+1.5f,p.Y+1.6f,p.Z+1.5f,3.5f,state.X,state.Z,eye)||
           !RenderView.AboveHorizon(p.Y+1.6f,p.Z+1.5f,3.5f,state.Z,eye))return;
        var root=roots[building];root.LocalPosition=new Vector3(x,p.Y,p.Z);root.LocalScale=new Vector3(1,1,1);
        float a=(z-state.Z)/CylinderWorld.Radius*.5f;root.LocalRotation=new Quaternion((float)Math.Sin(a),0,0,(float)Math.Cos(a));
        ExteriorParts(renderer,state,building,root,0);
    }
    internal void Interior(Renderer renderer,GameState state,Transform world,InteriorCamera camera,PortalSystem doors)
    {
        int building=BuildingWorld.Building(state.Space),type=state.BuildingTypes[building];float size=BuildingWorld.Size(state,state.Space),scale=size/4;
        NativeShadows.Scope(2);
        try{Part(renderer,type,2,world,Vector3.Zero,new Vector3(scale,1,scale));}finally{NativeShadows.Scope(0);}
        for(int wall=0;wall<4;wall++)if(camera.WallVisible(wall))Part(renderer,type,3+wall,world,Vector3.Zero,new Vector3(scale,1,scale));
        // Keep the doorway legible even when the front wall is cut away.
        if(camera.WallVisible(2)||doors.Openness(building)>0)Part(renderer,type,1,world,new Vector3(size*.5f-.55f*scale,0,size+.01f),new Vector3(scale,1,1),doors.Openness(building)*1.4f);
    }
}
