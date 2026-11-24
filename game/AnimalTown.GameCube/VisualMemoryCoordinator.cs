using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;
using Dolphin.Diagnostics;

// One policy observes the real MEM1 allocator and asks each owning subsystem to
// reclaim only resources outside its current/doorway demand. Never sums native
// owner charges: shared textures can appear in more than one owner's accounting.
internal sealed class VisualMemoryCoordinator
{
    private readonly int[] snapshot=new int[5];
    private long nextSample;
    private bool tracedPressure;
    internal readonly VisualMemoryPressure Pressure=new VisualMemoryPressure();
    internal int Reclaims {get;private set;}
    internal void Sample(long now,bool force=false)
    {
        if(!force&&!Pressure.RequiresObservation&&now<nextSample)return;
        if(NativeMemory.Read(snapshot)==1){
            Pressure.Observe(snapshot[0],snapshot[3],snapshot[4],now);nextSample=now+100000;
            if(Pressure.UnderPressure!=tracedPressure){
                tracedPressure=Pressure.UnderPressure;
                DebugOverlay.WriteLine("ANIMALTOWN MEMORY "+(tracedPressure?"PRESSURE":"RECOVERED")+" free "+snapshot[0]+" libc "+snapshot[1]+" arena "+snapshot[2]+" reserved "+Pressure.ReservedBytes+" requested "+Pressure.WaitingPeakBytes+" failures "+snapshot[3]+" last_failed "+Pressure.FailedRequestBytes);
            }
        }
    }
    internal void Prepare(GameState state,PortalSystem doors,bool outdoorDemand,
        LowPolyTrees trees,DroppedItemRenderer items,BuildingRenderer buildings,long now,long deadline)
    {
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Visual residency changes require EndFrame");
        Sample(now);
        buildings.Memory=Pressure;
        // Each call retires at most one category/part. Memory pressure bypasses
        // idle grace, never the pin for a scene being drawn or a door crossing.
        if(Clock.Timestamp<deadline){
            bool reclaimed=Pressure.UnderPressure&&TextureCache.TrimOneUnused();
            if(!reclaimed)reclaimed=items.ReleaseUnused(outdoorDemand,now,Pressure.UnderPressure);
            if(!reclaimed)reclaimed=trees.ReleaseUnused(outdoorDemand,now,Pressure.UnderPressure);
            if(!reclaimed)reclaimed=buildings.ReleaseUnused(state,doors,now,Pressure.UnderPressure);
            if(reclaimed){Reclaims++;Sample(now,true);}
        }
        buildings.PrepareResidency(state,doors,now,deadline,Pressure.UnderPressure,allowOptional:false);
    }
    internal void PrepareOptionalBuildings(GameState state,PortalSystem doors,BuildingRenderer buildings,long now,long deadline,bool essentialWorkPending=false)
    {
        if(essentialWorkPending||buildings.RequiredWorkPending)return;
        buildings.PrepareResidency(state,doors,now,deadline,Pressure.UnderPressure,allowOptional:true);
    }
}
