namespace AnimalTown.Jobs;

public sealed class MovingResident
{
    public JobIdentity Id;
    public string Name="",Phrase="";
    public int Species,Outfit,Personality,Remaining,Budget,Conversations;
    public bool Fresh;
}
public struct OfflineNpcMove
{
    public JobIdentity Id;
    public ulong FirstTown,SecondTown;
    public int FirstSlot,SecondSlot,Phase;
    public bool FirstSide;
    public MovingResident? First,Second;
    public bool Pending=>Phase>0&&Phase<4;
}
public sealed partial class OfflineJobLedger
{
    public OfflineNpcMove NpcMove {get;internal set;}
    public bool NpcInTransit(int slot)=>NpcMove.Pending&&slot==(NpcMove.FirstSide?NpcMove.FirstSlot:NpcMove.SecondSlot);
    private static void WriteMoving(JobWireWriter w,MovingResident? n)
    {w.Int(n==null?0:1);if(n==null)return;w.Key(n.Id);w.Text(n.Name??"");w.Text(n.Phrase??"");w.Int(n.Species);w.Int(n.Outfit);w.Int(n.Personality);w.Int(n.Remaining);w.Int(n.Budget);w.Int(n.Conversations);w.Int(n.Fresh?1:0);}
    private static MovingResident? ReadMoving(JobWireReader r,bool legacy)
    {if(!legacy&&r.Range(0,1)==0)return null;return new MovingResident{Id=r.Key(),Name=r.Text(24),Phrase=r.Text(32),Species=r.Range(0,5),Outfit=r.Range(0,Catalog.Items.Length-1),Personality=r.Range(0,5),Remaining=r.Range(0,5000),Budget=r.Range(0,1000),Conversations=r.Range(0,15),Fresh=r.Range(0,1)==1};}
    private void WriteNpcMove(JobWireWriter w)
    {var m=NpcMove;w.Key(m.Id);w.ULong(m.FirstTown);w.ULong(m.SecondTown);w.Int(m.FirstSlot);w.Int(m.SecondSlot);w.Int(m.Phase);w.Int(m.FirstSide?1:0);WriteMoving(w,m.First);WriteMoving(w,m.Second);}
    private void ReadNpcMove(JobWireReader r,bool legacy=false)
    {
        NpcMove=new OfflineNpcMove{Id=r.Key(),FirstTown=r.ULong(),SecondTown=r.ULong(),FirstSlot=r.Range(0,5),SecondSlot=r.Range(0,5),Phase=r.Range(0,4),FirstSide=r.Range(0,1)==1,First=ReadMoving(r,legacy),Second=ReadMoving(r,legacy)};
        var m=NpcMove;if(m.Phase>0&&(!m.Id.IsValid||m.First==null||m.Second==null||!m.First.Id.IsValid||!m.Second.Id.IsValid||m.First.Id==m.Second.Id||m.FirstTown==m.SecondTown||TownId!=(m.FirstSide?m.FirstTown:m.SecondTown)))throw new InvalidOperationException("Invalid villager moving receipt");
    }
}
public sealed partial class OfflineTownTravel
{
    private static MovingResident Moving(GameState town,int npc)
    {var n=town.Town.Residents[npc];return new MovingResident{Id=town.Town.Jobs.Npc(npc),Name=n.Name,Phrase=n.Phrase,Species=town.Town.Jobs.Species(npc),Outfit=town.Town.Jobs.Outfit(npc),Personality=(int)town.Town.JobState.Resident(npc).Personality,Remaining=town.Town.JobState.Resident(npc).RemainingTicks,Budget=town.Town.JobState.Resident(npc).AcreBudgetTicks,Conversations=town.Town.JobState.Resident(npc).ConversationCount,Fresh=town.Town.JobState.Resident(npc).FreshGenerationEnabled};}
    // Both roster choices are explicit. A full town exchanges the two selected
    // neighbours; no unrelated villager is silently evicted or destroyed.
    public GameState ExchangeResidents(int homeSlot,int destinationSlot,long now,bool visiting=false)
    {
        TakeSnapshots(out var a,out var b);var x=a.Town.Jobs;var y=b.Town.Jobs;
        if((uint)homeSlot>=6||(uint)destinationSlot>=6||x.NpcMove.Pending||y.NpcMove.Pending||!x.CanAllocate(1))throw new InvalidOperationException("Finish the current villager move first");
        for(int p=0;p<GameState.ProfileCount;p++)if(a.Profiles[p].Created&&a.Profiles[p].Tutorial<7||b.Profiles[p].Created&&b.Profiles[p].Tutorial<7)throw new InvalidOperationException("Finish opening employment before moving neighbours");
        if(!x.CanRememberNpcPair(x.Npc(homeSlot),y.Npc(destinationSlot))||!y.CanRememberNpcPair(x.Npc(homeSlot),y.Npc(destinationSlot)))throw new InvalidOperationException("Town address book is full");
        for(int p=0;p<OfflineJobLedger.OwnerCount;p++)if((p<GameState.ProfileCount?a.Profiles[p].Created:x.GuestProfile!=null)&&!x.CanAdoptNpc(p,y.Npc(destinationSlot))||(p<GameState.ProfileCount?b.Profiles[p].Created:y.GuestProfile!=null)&&!y.CanAdoptNpc(p,x.Npc(homeSlot)))throw new InvalidOperationException("Villager relationship history is full");
        var m=new OfflineNpcMove{Id=x.Allocate(),FirstTown=x.TownId,SecondTown=y.TownId,FirstSlot=homeSlot,SecondSlot=destinationSlot,Phase=1,FirstSide=true,First=Moving(a,homeSlot),Second=Moving(b,destinationSlot)};x.NpcMove=m;SaveChecked(first,a);
        return ContinueMove(a,b,now,visiting);
    }
    public GameState RecoverResidentMove(long now,bool visiting=false)
    {TakeSnapshots(out var a,out var b);return ContinueMove(a,b,now,visiting);}
    private GameState ContinueMove(GameState a,GameState b,long now,bool visiting)
    {
        var x=a.Town.Jobs;var y=b.Town.Jobs;var m=x.NpcMove;
        if(m.Phase==0||m.SecondTown!=y.TownId||!m.FirstSide||y.NpcMove.Pending&&y.NpcMove.Id!=m.Id)throw new InvalidOperationException("Insert both cards for the recorded villager move");
        if(y.NpcMove.Id!=m.Id){m.FirstSide=false;y.NpcMove=m;SaveChecked(second,b);}
        if(x.NpcMove.Phase==1){ApplyMovedResident(a,m.FirstSlot,m.First!,m.Second!,m.SecondTown,now);m=x.NpcMove;m.Phase=2;x.NpcMove=m;SaveChecked(first,a);}
        if(y.NpcMove.Phase==1){ApplyMovedResident(b,m.SecondSlot,m.Second!,m.First!,m.FirstTown,now);m=y.NpcMove;m.Phase=3;y.NpcMove=m;SaveChecked(second,b);}
        if(x.NpcMove.Phase!=4){m=x.NpcMove;m.Phase=4;x.NpcMove=m;SaveChecked(first,a);}
        if(y.NpcMove.Phase!=4){m=y.NpcMove;m.Phase=4;y.NpcMove=m;SaveChecked(second,b);}
        var result=visiting?b:a;result.ResidentJobs.Enable(result.Town.Jobs.TownId,now);result.ResidentJobs.Travel=this;knownFirst=a;knownSecond=b;return result;
    }
    private static void ApplyMovedResident(GameState s,int slot,MovingResident leaving,MovingResident arriving,ulong otherTown,long now)
    {
        var l=s.Town.Jobs;if(l.Npc(slot)==arriving.Id)return;if(l.Npc(slot)!=leaving.Id)throw new InvalidOperationException("A moved resident was replaced on another copy of this card");
        s.CaptureProfile();
        for(int p=0;p<OfflineJobLedger.OwnerCount;p++){
            if(p==OfflineJobLedger.Visitor&&l.GuestProfile==null||p<GameState.ProfileCount&&!s.Profiles[p].Created)continue;var profile=s.ProfileAt(p);l.RememberRelation(p,leaving.Id,profile.Relationships[slot*4],profile.Relationships[slot*4+3]);
            int relation=l.RelationSlot(p,arriving.Id,false);profile.Relationships[slot*4]=relation<0?0:l.Relations[relation].Friendship;profile.Relationships[slot*4+1]=0;profile.Relationships[slot*4+3]=relation<0?0:l.Relations[relation].Met;profile.MetMask&=~(1<<slot);l.RememberRelation(p,arriving.Id,profile.Relationships[slot*4],profile.Relationships[slot*4+3]);
        }
        l.SetNpcIdentity(slot,arriving.Id);l.SetOutfit(slot,arriving.Outfit);l.NpcSpecies[slot]=arriving.Species;
        var resident=s.Town.Residents[slot];resident.Name=arriving.Name;resident.Phrase=arriving.Phrase;resident.Space=0;resident.PosX=resident.HomeX+1.5f;resident.PosZ=resident.HomeZ+4.5f;resident.X=(int)resident.PosX;resident.Z=(int)resident.PosZ;resident.JourneyPhase=0;resident.JourneyBuilding=-1;resident.JourneyNextX=resident.JourneyNextZ=-1;resident.Dwell=0;
        s.Town.JobState.Resident(slot).Restore(arriving.Personality,arriving.Remaining,arriving.Budget,arriving.Conversations,arriving.Fresh?1:0);l.LastMovedNpc=leaving.Id;l.LastMovedTown=otherTown;l.RememberNpc(leaving.Id,otherTown,leaving.Name);l.RememberNpc(arriving.Id,l.TownId,arriving.Name);
        for(int i=0;i<l.Offers.Length;i++)if(l.Offers[i].Requester==leaving.Id)l.Offers[i]=default;
        // Shared favors belong to a requester/home acre. Earned obligations are
        // independent and survive this release of the departed NPC's live slot.
        int shared=OfflineJobLedger.SharedStart+slot;var job=l.Jobs[shared];if(job.Occupying){if(job.Status!=JobStatus.CompletedRetained&&job.Status!=JobStatus.RewardOwed&&job.Status!=JobStatus.ReplyOwed)s.ResidentJobs.RecordEnd(job,JobEndReason.WorldRemoved,now);int reward=l.FindReward(job.Obligation);if(reward>=0&&l.Rewards[reward].State==JobRewardState.Reserved)l.Rewards[reward]=default;l.Jobs[shared]=default;}
        s.RestoreProfile(s.ActiveProfile);s.Town.NotifyCastersChanged();l.Revision++;
    }
}
