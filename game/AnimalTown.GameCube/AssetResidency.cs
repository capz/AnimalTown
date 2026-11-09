using System;
using Dolphin.Graphics;
using AnimalTown;

internal sealed class AssetResidency
{
    private readonly AssetDemand demand=new AssetDemand();
    private readonly bool[] registered=new bool[AssetDemand.Capacity],ready=new bool[AssetDemand.Capacity];
    internal void Register(int slot,Model model,string path,bool permanent=false)
    {
        if(NativeAssetResidency.Capture(slot,model.NativeBytes,permanent?-1:slot<AssetDemand.Species?120:0,path)!=1)throw new InvalidOperationException("Ambiguous model residency registration");
        registered[slot]=ready[slot]=true;
    }
    internal void Forget(int slot)
    {if(!registered[slot])return;if(NativeAssetResidency.Forget(slot)!=1)throw new InvalidOperationException("Residency release failed");registered[slot]=ready[slot]=false;}
    internal bool Ready(int slot)=>registered[slot]&&ready[slot];
    internal bool[] ReadyModels=>ready;
    internal bool AllReady {get{for(int i=0;i<ready.Length;i++)if(registered[i]&&!ready[i])return false;return true;}}
    internal bool Backed=>NativeAssetResidency.Stats(0)==0;
    internal void Prepare(GameState state,PortalSystem doors,bool allWarm,bool startup=false)
    {
        demand.Prepare(state,doors,allWarm);
        if(NativeAssetResidency.Advance(demand.Priority,startup?1:0)<0)throw new InvalidOperationException("Asset transfer failed");
        for(int i=0;i<ready.Length;i++)if(registered[i]){int status=NativeAssetResidency.State(i);ready[i]=(status&1)!=0;if((status&16)!=0)throw new InvalidOperationException("The original asset could not be restored from disc");}
        // Distant NPC journeys never depend on render-asset residency.
        demand.UpdateDoorReadiness(state,doors,ready);
    }
}
