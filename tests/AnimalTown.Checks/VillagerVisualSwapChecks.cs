using AnimalTown;
using Dolphin.Graphics;

internal static class VillagerVisualSwapChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(7319){Name="Test resident",Wallet=1234,Jobs=1,Species=0};
        state.Town.Residents[0].Name="Original name";state.Town.Residents[0].Request=14;
        byte[] before=SaveCodec.Encode(state);
        var selection=new VillagerAppearanceSelection();
        check(!selection.Apply(state,1,0),"visuals reject an appearance used by the player");
        check(!selection.Apply(state,-1,6)&&!selection.Apply(state,1,99),"visuals reject invalid targets and appearances");
        check(selection.Apply(state,1,6)&&selection.Current(state,1)==6,"visual selection changes just the chosen actor");
        check(selection.Used(state,6)&&!selection.Apply(state,2,6),"an assigned appearance cannot be duplicated");
        check(SaveCodec.Encode(state).SequenceEqual(before),"visual selection leaves the entire serialized town/player/jobs unchanged");
        var actors=new ModelInstance?[BuildingWorld.Actors];var shared=new Model();
        for(int i=0;i<actors.Length;i++)actors[i]=shared.CreateInstance(ModelMemoryPlan.MaximumInstanceBytes);
        var originals=(ModelInstance?[])actors.Clone();
        var memory=new VisualMemoryPressure();memory.Observe(12*1024*1024,0,0,0);
        var lengths=Enumerable.Repeat(65536,VillagerAppearance.Count).ToArray();
        var peaks=Enumerable.Repeat(512*1024,VillagerAppearance.Count).ToArray();
        TaskCompletionSource<Model>? completion=null;
        var swap=new VillagerVisualSwap(actors,memory,lengths,peaks,(_,token)=>{
            completion=new TaskCompletionSource<Model>();token.Register(()=>completion.TrySetCanceled(token));return completion.Task;
        });
        check(swap.Request(state,1,6,0)&&swap.Busy&&memory.ReservedBytes==peaks[6],"swap reserves a bounded peak before asynchronous loading");
        check(swap.Pump(state,0)==-1&&actors[1]==originals[1]&&!originals[1]!.Disposed,"pending swap keeps the previous visual alive");
        check(!swap.Request(state,2,7,0),"only one visual load owns admission at a time");
        var first=new Model();completion!.SetResult(first);
        check(swap.Pump(state,1)==1&&actors[1]!.Model==first&&originals[1]!.Disposed,"complete visual replaces only its target and releases the old instance");
        check(!shared.Disposed&&actors.Where((a,i)=>i!=1).All(a=>a!.Model==shared&&!a.Disposed),"shared species models and other actors remain usable");
        check(!swap.Request(state,2,6,2),"runtime admission rejects an appearance that became used");
        memory.Observe(12*1024*1024,0,0,3);
        check(swap.Request(state,1,7,3),"replacement can be changed repeatedly");
        var broken=new Model{FailInstance=true};completion!.SetResult(broken);var previous=actors[1];
        check(swap.Pump(state,4)==-1&&actors[1]==previous&&broken.Disposed&&!first.Disposed,"instance failure disposes the candidate and preserves the working visual");
        check(swap.Selection.Current(state,1)==6,"failed swaps do not publish their selection");
        check(memory.ReservedBytes==0&&!swap.Busy,"failed load releases its admission ticket");
        memory.Observe(12*1024*1024,0,0,2000000);
        check(swap.Request(state,1,7,2000000),"failed allocation can retry after the allocator backoff");
        var wrong=new Model{PackageBytes=65568};completion!.SetResult(wrong);
        check(swap.Pump(state,2000001)==-1&&wrong.Disposed&&actors[1]==previous,"stale/mismatched packages cannot replace a working actor");
        var retired=new List<Model>{first};
        for(int i=0;i<20;i++){
            memory.Observe(12*1024*1024,0,0,2000010+i*2);
            int next=swap.Selection.NextUnused(state,6+i%5,1);
            check(swap.Request(state,1,next,2000010+i*2),"repeated swap is admitted");
            var replacement=new Model();completion!.SetResult(replacement);
            check(swap.Pump(state,2000011+i*2)==1&&retired.All(m=>m.Disposed),"repeated swap retires every previous detached package");
            retired.Add(replacement);
        }
        check(SaveCodec.Encode(state).SequenceEqual(before),"repeated runtime swaps preserve every serialized resident/player/job field");
        memory.Observe(100*1024,0,0,3000000);
        int available=swap.Selection.NextUnused(state,-1,1);
        check(!swap.Request(state,1,available,3000000)&&!swap.Busy,"insufficient memory leaves existing visuals intact");
        memory.Observe(12*1024*1024,0,0,4000000);
        check(swap.Request(state,1,available,4000000),"essential visual admission can proceed once physical headroom returns");
        foreach(var actor in actors)actor!.Dispose();swap.Dispose();
        check(memory.ReservedBytes==0&&retired.All(m=>m.Disposed)&&!shared.Disposed,"teardown drains pending load, releases tickets, and disposes owned models only");
        shared.Dispose();
        foreach(string name in VillagerAppearance.Models){
            check(TexturePolicy.DefaultForModel(name)==TextureResolution.Size128,"every appearance uses the original villagers' 128px runtime texture policy");
        }
    }
}
