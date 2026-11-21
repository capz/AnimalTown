using AnimalTown.Jobs;
namespace AnimalTown;

public sealed partial class GameState
{
    public int ResidentSpecies(int npc)=>Town.Jobs.Initialized?Town.Jobs.Species(npc):npc;
    public bool Visiting=>ActiveProfile==OfflineJobLedger.Visitor;
    public VillagerProfile ProfileAt(int profile)=>profile==OfflineJobLedger.Visitor?Town.Jobs.GuestProfile??throw new InvalidOperationException("Visiting capsule is unavailable"):Profiles[profile];
    public bool ProfileLocked(int profile)=>Town.Jobs.Trip.Pending&&Town.Jobs.Trip.HomeSide&&Town.Jobs.Trip.HomeProfile==profile;
    internal int SuspendVisitor()
    {if(!Visiting)return -1;CaptureProfile();int anchor=Town.Jobs.Trip.ResidentAnchor;RestoreProfile(anchor);return OfflineJobLedger.Visitor;}
    internal void ResumeVisitor(int profile){if(profile==OfflineJobLedger.Visitor&&Town.Jobs.GuestProfile!=null)RestoreProfile(profile);}
    internal void ActivateSavedVisitor(){if(Town.Jobs.ActiveVisitor&&Town.Jobs.GuestProfile!=null)RestoreProfile(OfflineJobLedger.Visitor);}
    public void AdvanceJobClock(int ticks)
    {for(int n=0;n<6;n++)if(!Town.Jobs.Initialized||!Town.Jobs.NpcInTransit(n))Town.JobState.Resident(n).Advance(ticks);}
    public void ObserveJobAcre(int acre)
    {
        if(!Visiting){Town.JobState.ObserveOutdoorAcre(ActiveProfile,acre);return;}
        int previous=Town.Jobs.GuestAcre;Town.Jobs.GuestAcre=acre;if(previous<0||previous==acre)return;for(int n=0;n<6;n++)Town.JobState.Resident(n).AcreEntered();
    }
}
