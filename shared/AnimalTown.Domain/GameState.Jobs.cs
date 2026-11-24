using AnimalTown.Jobs;

namespace AnimalTown;

public sealed partial class GameState
{
    internal void MigrateJobTutorial(int catalogVersion)
    {if(catalogVersion!=1)return;for(int p=0;p<ProfileCount;p++)if(Profiles[p].Tutorial==6)Profiles[p].Tutorial=7;if(Tutorial==6)Tutorial=7;}
    private VillagerJobService? residentJobs;
    public VillagerJobService ResidentJobs=>residentJobs??(residentJobs=new VillagerJobService(this));
    public void EnsureJobIdentities(ulong townNonce)
    {
        CaptureProfile();
        Town.Jobs.Initialize(townNonce);
        for(int i=0;i<ProfileCount;i++)if(Profiles[i].Created){Town.Jobs.EnsurePlayer(i);for(int n=0;n<6;n++)Town.Jobs.RememberRelation(i,Town.Jobs.Npc(n),Profiles[i].Relationships[n*4],Profiles[i].Relationships[n*4+3]);}
    }
    public bool IsJobItem(int slot)=>Town.Jobs.HasBinding(ActiveProfile,slot);
    public bool SwapPockets(int a,int b)
    {
        if((uint)a>=Capacity||(uint)b>=Capacity)return false;
        int item=Pockets[a];Pockets[a]=Pockets[b];Pockets[b]=item;
        if(Town.Jobs.Initialized){var key=Town.Jobs.Binding(ActiveProfile,a);Town.Jobs.SetBinding(ActiveProfile,a,Town.Jobs.Binding(ActiveProfile,b));Town.Jobs.SetBinding(ActiveProfile,b,key);}
        return true;
    }
}
