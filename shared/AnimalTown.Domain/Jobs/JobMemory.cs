namespace AnimalTown.Jobs;

// Optional platform collection point. Native nonmoving heaps must reclaim
// short-lived normalization buffers before retaining another text slab.
// This runs only when retaining another packed body/slab, never during a tick.
public static class JobMemory
{
    public static Action? ReclaimTemporaryText {get;set;}
    internal static void Reclaim()=>ReclaimTemporaryText?.Invoke();
}
