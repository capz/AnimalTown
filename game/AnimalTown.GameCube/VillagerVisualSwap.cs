using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Graphics;

// Own only detached visual packages. Existing shared species residency/LOD and
// all saved NPC identities remain untouched. Publish a complete instance first.
internal interface IVillagerVisualLoader
{
    Task<Model> Load(string name,CancellationToken token);
}

internal sealed class VillagerVisualSwap : IDisposable
{
    private sealed class ResourceLoader : IVillagerVisualLoader
    {public Task<Model> Load(string name,CancellationToken token)=>ModelResources.LoadLodAsync(name,0,token);}
    internal readonly VillagerAppearanceSelection Selection=new VillagerAppearanceSelection();
    private readonly ModelInstance?[] actors;
    private readonly Model?[] owned=new Model?[BuildingWorld.Actors];
    private readonly VisualMemoryPressure memory;
    private readonly IVillagerVisualLoader loader;
    private readonly int[] lengths,peaks;
    private Task<Model>? pending;
    private CancellationTokenSource? cancellation;
    private int target=-1,appearance=-1,ticket;
    private bool stopped;
    internal bool Busy=>pending!=null;
    internal string Status {get;private set;}="Unused appearances only. Session only.";
    internal VillagerVisualSwap(ModelInstance?[] actors,VisualMemoryPressure memory,int[] lengths,int[] peaks,
        IVillagerVisualLoader? loader=null)
    {
        if(lengths.Length!=VillagerAppearance.Count||peaks.Length!=lengths.Length)throw new ArgumentException("Appearance memory catalog size");
        this.actors=actors;this.memory=memory;this.lengths=lengths;this.peaks=peaks;
        this.loader=loader??new ResourceLoader();
    }
    internal static VillagerVisualSwap Open(ModelInstance?[] actors,VisualMemoryPressure memory)
    {
        byte[] bytes=File.ReadAllBytes(ContentPaths.Asset("villager-appearance-memory.bin"));
        if(bytes.Length!=8+VillagerAppearance.Count*8||Read(bytes,0)!=0x41564d31||Read(bytes,4)!=VillagerAppearance.Count)
            throw new InvalidDataException("Invalid appearance memory catalog");
        var lengths=new int[VillagerAppearance.Count];var peaks=new int[lengths.Length];
        for(int i=0;i<lengths.Length;i++){
            lengths[i]=Read(bytes,8+i*8);peaks[i]=Read(bytes,12+i*8);
            if(lengths[i]<160||lengths[i]>ModelMemoryPlan.MaximumSharedBytes||peaks[i]<lengths[i]||peaks[i]>8*1024*1024)
                throw new InvalidDataException("Invalid appearance memory bound");
        }
        return new VillagerVisualSwap(actors,memory,lengths,peaks);
    }
    private static int Read(byte[] bytes,int at)=>bytes[at]|bytes[at+1]<<8|bytes[at+2]<<16|bytes[at+3]<<24;
    internal bool Request(GameState state,int actor,int next,long now)
    {
        if(stopped||Busy)return false;
        if(!VillagerAppearanceSelection.Present(state,actor)||Selection.Used(state,next)){Status="Choose an unused appearance.";return false;}
        if(!memory.TryReserve(peaks[next],now,true,out ticket)){Status="Not enough memory yet. Try again shortly.";return false;}
        target=actor;appearance=next;cancellation=new CancellationTokenSource();
        try{pending=loader.Load(VillagerAppearance.Models[next],cancellation.Token);Status="Loading "+VillagerAppearance.Names[next]+"...";return true;}
        catch(Exception){ReleasePending();Status="Could not load appearance. Existing visual kept.";return false;}
    }
    internal int Pump(GameState state,long now)
    {
        if(pending==null||!pending.IsCompleted)return -1;
        Model? model=null;ModelInstance? instance=null;
        try{
            model=pending.GetAwaiter().GetResult();
            if(model.PackageBytes!=lengths[appearance])throw new InvalidDataException("Appearance package differs from memory plan");
            if(!VillagerAppearanceSelection.Present(state,target)||Selection.Used(state,appearance))throw new InvalidOperationException("Appearance selection changed");
            instance=model.CreateInstance(ModelMemoryPlan.MaximumInstanceBytes);
            if(!Selection.Apply(state,target,appearance))throw new InvalidOperationException("Appearance is no longer unused");
            int changed=target;var old=actors[target];var oldModel=owned[target];
            actors[target]=instance;owned[target]=model;instance=null;model=null;
            memory.Complete(ticket);ticket=0;
            old?.Dispose();oldModel?.Dispose();
            Status="Applied "+VillagerAppearance.Names[appearance]+". Resident data preserved.";
            return changed;
        }catch(Exception error){
            instance?.Dispose();model?.Dispose();
            if(error is OutOfMemoryException)memory.AllocationFailed(peaks[appearance],now);
            Status="Could not load appearance. Existing visual kept.";return -1;
        }finally{ReleasePending();}
    }
    private void ReleasePending()
    {memory.Cancel(ticket);ticket=0;pending=null;cancellation?.Dispose();cancellation=null;target=appearance=-1;}
    // Called after the town has disposed its actor instances.
    public void Dispose()
    {
        if(stopped)return;stopped=true;
        if(pending!=null){cancellation!.Cancel();try{pending.GetAwaiter().GetResult().Dispose();}catch(Exception){}finally{ReleasePending();}}
        for(int actor=0;actor<owned.Length;actor++){owned[actor]?.Dispose();owned[actor]=null;}
    }
}
