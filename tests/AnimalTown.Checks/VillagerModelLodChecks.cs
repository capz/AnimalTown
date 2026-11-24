using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Timing;

internal static class ModelResources
{
    internal const int DefaultBudget=3*1024*1024;
    internal static readonly List<TaskCompletionSource<Model>> Requests=new();
    internal static Task<Model> LoadLodAsync(string name,int lod,CancellationToken token=default)
    {
        var source=new TaskCompletionSource<Model>();Requests.Add(source);token.Register(()=>source.TrySetCanceled(token));return source.Task;
    }
}

internal static class VillagerModelLodChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var models=Enumerable.Range(0,6).Select(_=>new Model()).ToArray();
        var actors=new ModelInstance[7];for(int i=0;i<6;i++)actors[i+1]=models[i].CreateInstance(512*1024);actors[0]=models[0].CreateInstance(512*1024);
        var residency=new ModelResidency();for(int i=0;i<6;i++)residency.Register(i,models[i],0);
        var pressure=new VisualMemoryPressure();pressure.Observe(2*1024*1024,0,0,0);residency.Memory=pressure;
        var owner=new VillagerModelLod(models,actors,residency){Memory=pressure};
        var state=new GameState(17){Species=0};var view=new ActorFrameVisibility();
        view.Visible[2]=true;view.GeometryLod[2]=1;owner.Require(state,view);Clock.Timestamp=0;
        var oldModel=models[1];var oldInstance=actors[2];int requests=ModelResources.Requests.Count;
        owner.Pump(0,1000);
        check(owner.PendingSpecies==1&&ModelResources.Requests.Count==requests+1&&pressure.ReservedBytes==owner.ReplacementPeak(1),"LOD reserves its measured full-variant replacement peak and remains active within two MiB of headroom");
        owner.Pump(0,1000);check(ReferenceEquals(models[1],oldModel)&&ReferenceEquals(actors[2],oldInstance)&&!oldModel.Disposed,"incomplete coarse load retains the original drawable model and instance");
        var coarse=new Model{PackageBytes=32000,InstanceBytes=oldInstance.NativeBytes};ModelResources.Requests[^1].SetResult(coarse);owner.Pump(0,1000);
        check(ReferenceEquals(models[1],coarse)&&ReferenceEquals(actors[2].Model,coarse)&&oldModel.Disposed&&oldInstance.Disposed&&owner.Level(1)==1&&residency.Ready(1),"completed LOD publishes one replacement owner and retires both old native owners");
        check(actors[2].NativeBytes==oldInstance.NativeBytes,"an unskinned bunny admits useful immutable geometry savings even when its full/coarse instance sizes are identical");
        check(owner.ConsumeChangedActors()==4&&owner.ConsumeChangedActors()==0,"replacement requests one clip/attention rebind for exactly its NPC actor");
        check(pressure.ReservedBytes==owner.ReplacementPeak(1),"successful replacement remains charged until a fresh physical allocator sample");
        pressure.Observe(8*1024*1024,0,0,1);check(pressure.ReservedBytes==0,"physical sampling releases the completed LOD reservation");
        view.GeometryLod[1]=1;view.Visible[1]=true;owner.Require(state,view);owner.Pump(1,1000);
        check(owner.PendingSpecies<0&&owner.Level(0)==0,"selected player species never loads an additional coarse copy for its NPC");
        view.GeometryLod[2]=0;owner.Require(state,view);owner.Pump(2,1000);
        var failed=new Model{FailInstance=true};ModelResources.Requests[^1].SetResult(failed);owner.Pump(2,1000);
        check(failed.Disposed&&ReferenceEquals(models[1],coarse)&&!coarse.Disposed&&pressure.ReservedBytes==0&&pressure.UnderPressure,"failed replacement frees the candidate, cancels debt and preserves the old drawable cohort");
        requests=ModelResources.Requests.Count;owner.Pump(2000000,2001000);
        check(ModelResources.Requests.Count==requests,"global memory pressure suppresses optional LOD allocation after local retry delay");
        pressure.Observe(8*1024*1024,0,0,2000000);pressure.Observe(8*1024*1024,0,0,5000000);
        owner.Pump(5000000,5001000);check(owner.PendingSpecies==1,"stable recovered memory admits the deferred detail upgrade");
        var doors=new PortalSystem();var crossing=doors.Actors[0];crossing.Phase=DoorPhase.Waiting;crossing.Source=new ActorLocation(0,10,10,0);crossing.Destination=new ActorLocation(17,2,2,0);
        state.Town.Residents[1].Space=17;view.Visible[2]=false;owner.Require(state,view,false,doors);
        check(!owner.FullForSpace(state,doors,17),"a doorway destination with an invisible coarse resident waits for full geometry before committing");
        var full=new Model();ModelResources.Requests[^1].SetResult(full);owner.Pump(5000000,5001000);
        check(owner.FullForSpace(state,doors,17)&&owner.Level(1)==0&&coarse.Disposed,"destination full demand survives outside visibility and publishes before portal readiness");
        pressure.Observe(8*1024*1024,0,0,5000001);crossing.Phase=DoorPhase.Idle;state.Town.Residents[1].Space=0;view.Visible[2]=true;view.GeometryLod[2]=1;
        owner.Require(state,view);owner.Paused=true;requests=ModelResources.Requests.Count;owner.Pump(5000001,5001001);
        check(ModelResources.Requests.Count==requests&&owner.FullReady,"an active UV probe blocks replacement and retains its full model references");
        owner.Paused=false;owner.Pump(5000001,5001001);check(owner.PendingSpecies==1,"leaving the UV probe permits a fresh optional replacement");
        owner.Require(state,view,true,doors);check(ModelResources.Requests[^1].Task.IsCanceled,"a full inspection request cancels an obsolete pending coarse load");
        owner.Pump(5000001,5001001);check(owner.PendingSpecies<0&&owner.FullReady,"canceled coarse work drains without changing the full cohort");
        owner.Require(state,view);owner.Pump(6000001,6001001);
        owner.Dispose();check(owner.PendingSpecies<0&&pressure.ReservedBytes==0&&!full.Disposed,"teardown drains canceled asset work before releasing its reservation and keeps adopted models with the town owner");
        residency.Stop();
        SharedCohort(check);
    }
    private static void SharedCohort(Action<bool,string> check)
    {
        var state=new GameState(17){Species=0};
        state.SetTownVisitors(new[]{new SavedVisitor(20,0,new JobIdentity(20,1),"First","Hello!",1,0),new SavedVisitor(21,0,new JobIdentity(21,1),"Second","Hello!",1,0)});
        var models=Enumerable.Range(0,6).Select(_=>new Model()).ToArray();var actors=new ModelInstance?[BuildingWorld.Actors];
        actors[0]=models[0].CreateInstance(ModelMemoryPlan.MaximumInstanceBytes);
        for(int npc=0;npc<Town.NpcActorCount;npc++)if(state.NpcPresent(npc))actors[npc+1]=models[state.NpcSpecies(npc)].CreateInstance(ModelMemoryPlan.MaximumInstanceBytes);
        var residency=new ModelResidency();for(int species=0;species<6;species++)residency.Register(species,models[species],0);
        var memory=new VisualMemoryPressure();memory.Observe(4*1024*1024,0,0,0);residency.Memory=memory;
        var owner=new VillagerModelLod(models,actors,residency){Memory=memory};var view=new ActorFrameVisibility();
        int mask=0,count=0;
        for(int npc=0;npc<Town.NpcActorCount;npc++)if(state.NpcPresent(npc)&&state.NpcSpecies(npc)==1){int actor=npc+1;mask|=1<<actor;count++;view.Visible[actor]=true;view.GeometryLod[actor]=1;}
        check(count==3,"shared geometry fixture includes a local resident and two independently mapped same-species visitors");
        int nearActor=BuildingWorld.Actors-1;view.GeometryLod[nearActor]=0;owner.Require(state,view);int requests=ModelResources.Requests.Count;Clock.Timestamp=0;owner.Pump(0,1000);
        check(owner.PendingSpecies<0&&ModelResources.Requests.Count==requests,"any near same-species visitor pins the complete shared species to full geometry");
        view.GeometryLod[nearActor]=1;owner.Require(state,view);owner.Pump(0,1000);
        int expected=models[1].NativeBytes+models[1].PackageBytes+ModelMemoryPlan.LoadGuardBytes+count*models[1].InstanceBytes;
        check(owner.PendingSpecies==1&&memory.ReservedBytes==expected,"replacement admission reserves every instance of the shared species cohort");
        var originals=(ModelInstance?[])actors.Clone();var oldModel=models[1];var partial=new Model{PackageBytes=32000,FailInstanceAt=2};
        ModelResources.Requests[^1].SetResult(partial);owner.Pump(0,1000);
        check(partial.Disposed&&partial.LastInstance?.Disposed==true&&ReferenceEquals(models[1],oldModel)&&memory.ReservedBytes==0,"failure in a later visitor instance destroys the complete candidate transaction and preserves the old shared model");
        for(int actor=1;actor<actors.Length;actor++)if((mask&(1<<actor))!=0)check(ReferenceEquals(actors[actor],originals[actor])&&!originals[actor]!.Disposed,"partial replacement publishes no local or visiting NPC instance");
        memory.Observe(4*1024*1024,0,0,1000001);memory.Observe(4*1024*1024,0,0,4000001);owner.Pump(4000001,4001001);
        var coarse=new Model{PackageBytes=32000};ModelResources.Requests[^1].SetResult(coarse);owner.Pump(4000001,4001001);
        check(coarse.InstanceCreations==count&&ReferenceEquals(models[1],coarse)&&owner.Level(1)==1&&owner.ConsumeChangedActors()==mask,"a successful transaction swaps every shared-species actor and reports every clip/attention rebind atomically");
        for(int actor=1;actor<actors.Length;actor++)if((mask&(1<<actor))!=0)check(ReferenceEquals(actors[actor]!.Model,coarse)&&originals[actor]!.Disposed,"old visitor and resident instances retire only after the replacement cohort publishes");
        check(oldModel.Disposed&&residency.RestorePeak(1)==coarse.NativeBytes+count*coarse.InstanceBytes+ModelMemoryPlan.LoadGuardBytes,"ARAM restore accounts for all instances sharing the published coarse model");
        memory.Observe(4*1024*1024,0,0,4000002);view.GeometryLod[nearActor]=0;owner.Require(state,view);owner.Pump(4000002,4001002);
        var full=new Model();ModelResources.Requests[^1].SetResult(full);owner.Pump(4000002,4001002);owner.ConsumeChangedActors();
        memory.Observe(4*1024*1024,0,0,4000003);view.GeometryLod[nearActor]=1;owner.Require(state,view);owner.Pump(4000003,4001003);
        var duplicate=new Model();ModelResources.Requests[^1].SetResult(duplicate);owner.Pump(4000003,4001003);
        check(duplicate.Disposed&&duplicate.LastInstance?.Disposed==true&&ReferenceEquals(models[1],full)&&owner.Level(1)==0&&owner.ConsumeChangedActors()==0,"a coarse package with no aggregate geometry/instance saving is discarded without changing any actor");
        requests=ModelResources.Requests.Count;for(int i=0;i<10;i++){owner.Require(state,view);owner.Pump(5000000+i,5001000+i);}
        check(ModelResources.Requests.Count==requests,"a rejected no-op variant cannot trigger recurring disc loads at the distant threshold");
        owner.Dispose();residency.Stop();
    }
}
