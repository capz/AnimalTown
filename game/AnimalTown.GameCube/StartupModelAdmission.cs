using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;

// Owns admission, never the async model or its instances. The town publishes
// them atomically and only then completes this ticket; failure/teardown drains
// and disposes native owners before returning their reserved headroom.
internal sealed class StartupModelAdmission
{
    private readonly VisualMemoryPressure memory;
    private readonly int[] snapshot=new int[5];
    private readonly long[] retryAt=new long[ModelMemoryCatalog.Count];
    private ModelMemoryCatalog? catalog;
    private int active=-1,ticket,failureAtAdmission;
    internal int Active=>active;
    internal int ReservedPeak {get;private set;}
    internal StartupModelAdmission(VisualMemoryPressure memory,ModelMemoryCatalog? catalog=null)
    {this.memory=memory;this.catalog=catalog;}
    internal bool Begin(int species,bool selected,long now)
    {
        if(active>=0)throw new InvalidOperationException("A startup model already owns admission");
        if(species<0||species>=retryAt.Length)throw new ArgumentOutOfRangeException("species");
        if(now<retryAt[species])return false;
        catalog??=new ModelMemoryCatalog(File.ReadAllBytes(ContentPaths.Asset("villager-memory.bin")));
        int peak=catalog.StartupPeak(species,selected);
        if(!memory.TryReserve(peak,now,true,out ticket))return false;
        active=species;ReservedPeak=peak;failureAtAdmission=memory.FailureCount;return true;
    }
    internal void Validate(Model model)
    {
        if(active<0)throw new InvalidOperationException("No startup admission owns this package");
        catalog!.ValidatePackage(active,model.PackageBytes);
    }
    internal void Complete()
    {
        if(active<0)throw new InvalidOperationException("No startup model admission to complete");
        memory.Complete(ticket);ticket=0;active=-1;ReservedPeak=0;
    }
    internal bool Fail(Exception error,long now)
    {
        if(active<0)return false;
        int species=active,peak=ReservedPeak;
        bool allocationFailure=error is OutOfMemoryException;
        if(NativeMemory.Read(snapshot)==1){
            allocationFailure|=snapshot[3]!=failureAtAdmission;
            memory.Observe(snapshot[0],snapshot[3],snapshot[4],now);
        }
        Cancel();
        if(allocationFailure){retryAt[species]=now+VisualMemoryPressure.FailureBackoffUs;memory.AllocationFailed(peak,now);}
        return allocationFailure;
    }
    internal void Cancel()
    {memory.Cancel(ticket);ticket=0;active=-1;ReservedPeak=0;}
}
