namespace AnimalTown.Jobs;

public struct JobTerminal
{
    public JobIdentity Job,Owner,Requester;
    public JobTemplate Template;
    public JobEndReason Reason;
    public long At;
}
public sealed partial class OfflineJobLedger
{
    public const int TerminalCapacity=16;
    internal readonly JobTerminal[] Terminals=new JobTerminal[TerminalCapacity];
    internal int TerminalNext;
    internal readonly bool[] RemovedNpcs=new bool[32];
    public JobTerminal Terminal(int row)=>Terminals[(TerminalNext+TerminalCapacity-1-row)%TerminalCapacity];
    public bool PermanentlyRemoved(JobIdentity id)
    {for(int i=0;i<KnownNpcs.Length;i++)if(KnownNpcs[i]==id)return RemovedNpcs[i];return false;}
    private void WriteRecovery(JobWireWriter w)
    {
        for(int i=0;i<RemovedNpcs.Length;i++)w.Int(RemovedNpcs[i]?1:0);w.Int(TerminalNext);
        for(int i=0;i<Terminals.Length;i++){var e=Terminals[i];w.Key(e.Job);if(e.Job.IsEmpty)continue;w.Key(e.Owner);w.Key(e.Requester);w.Int((int)e.Template);w.Int((int)e.Reason);w.Long(e.At);}
    }
    private void ReadRecovery(JobWireReader r)
    {
        for(int i=0;i<RemovedNpcs.Length;i++)RemovedNpcs[i]=r.Range(0,1)==1;TerminalNext=r.Range(0,TerminalCapacity-1);
        for(int i=0;i<Terminals.Length;i++){var job=r.Key();if(job.IsEmpty)continue;Terminals[i]=new JobTerminal{Job=job,Owner=r.Key(false),Requester=r.Key(false),Template=(JobTemplate)r.Range(0,11),Reason=(JobEndReason)r.Range(1,(int)JobEndReason.ProfileDeleted),At=r.Long()};}
    }
}
public sealed partial class VillagerJobService
{
    internal void RecordEnd(VillagerJobRecord job,JobEndReason reason,long at=long.MinValue)
    {
        var owner=job.Shared?(job.Claimant.IsEmpty?job.AcceptedBy:job.Claimant):job.Owner;
        for(int i=0;i<ledger.Terminals.Length;i++)if(ledger.Terminals[i].Job==job.Id)return;
        ledger.Terminals[ledger.TerminalNext]=new JobTerminal{Job=job.Id,Owner=owner,Requester=job.Requester,Template=job.Template,Reason=reason,At=at!=long.MinValue?at:Enabled?Now:ledger.Watermark};ledger.TerminalNext=(ledger.TerminalNext+1)%ledger.Terminals.Length;
    }
    // Content/migration adapter: a proven removal is distinct from an absent
    // card, sleep or an unloaded model. It never substitutes a new NPC identity.
    public JobOutcome ReplaceResident(int slot,string name,string phrase,int species,JobPersonality personality,int outfit=0)
    {
        if(!Enabled||(uint)slot>=6||ledger.NpcInTransit(slot)||name.Length<1||name.Length>24||phrase.Length>32||(uint)species>=6||(uint)personality>=6||outfit!=0&&!Catalog.Has(outfit,ItemFlags.Wear)||!ledger.CanAllocate(1))return new JobOutcome(JobResult.InvalidContext);
        var old=ledger.Npc(slot);int known=-1;
        for(int i=0;i<ledger.KnownNpcs.Length;i++){if(ledger.KnownNpcs[i]==old){known=i;break;}if(ledger.KnownNpcs[i].IsEmpty&&known<0)known=i;}
        if(known<0)return new JobOutcome(JobResult.FullJobs);
        for(int p=0;p<OfflineJobLedger.OwnerCount;p++)if((p<GameState.ProfileCount?state.Profiles[p].Created:ledger.GuestProfile!=null)&&!ledger.CanAdoptNpc(p,default,true))return new JobOutcome(JobResult.FullJobs);
        state.CaptureProfile();var replacement=ledger.Allocate();ledger.RememberNpc(old,ledger.TownId,state.Town.Residents[slot].Name);ledger.RemovedNpcs[known]=true;
        for(int i=0;i<ledger.Jobs.Length;i++){var j=ledger.Jobs[i];if(j.Occupying&&j.Status!=JobStatus.RewardOwed&&j.Status!=JobStatus.CompletedRetained&&j.Status!=JobStatus.ReplyOwed&&(j.Requester==old||j.Target==old))CancelJob(i,JobEndReason.WorldRemoved);}
        for(int i=0;i<ledger.Offers.Length;i++)if(ledger.Offers[i].Requester==old||ledger.Offers[i].Target==old)ledger.Offers[i]=default;
        for(int p=0;p<OfflineJobLedger.OwnerCount;p++){
            if(p==OfflineJobLedger.Visitor&&ledger.GuestProfile==null||p<GameState.ProfileCount&&!state.Profiles[p].Created)continue;var profile=state.ProfileAt(p);ledger.RememberRelation(p,old,profile.Relationships[slot*4],profile.Relationships[slot*4+3]);ledger.RememberRelation(p,replacement,0,0);profile.Relationships[slot*4]=profile.Relationships[slot*4+1]=profile.Relationships[slot*4+2]=profile.Relationships[slot*4+3]=0;profile.MetMask&=~(1<<slot);
        }
        ledger.SetNpcIdentity(slot,replacement);ledger.SetOutfit(slot,outfit);ledger.NpcSpecies[slot]=species;ledger.FurnitureCacheItems[slot]=ledger.FurnitureCacheVersions[slot]=0;
        var resident=state.Town.Residents[slot];resident.Name=name;resident.Phrase=phrase;resident.PosX=resident.HomeX+1.5f;resident.PosZ=resident.HomeZ+4.5f;resident.X=(int)resident.PosX;resident.Z=(int)resident.PosZ;resident.Space=0;resident.JourneyPhase=0;resident.JourneyBuilding=-1;resident.JourneyNextX=resident.JourneyNextZ=-1;resident.Dwell=180;
        state.Town.JobState.Resident(slot).Restore((int)personality,0,0,0,1);state.RestoreProfile(state.ActiveProfile);InvalidateConversation();state.Town.NotifyCastersChanged();ledger.Revision++;return new JobOutcome(JobResult.Ok,replacement);
    }
    private JobOutcome CollectRemovedReward(JobIdentity id)
    {
        int at=ledger.FindReward(id);if(at<0)return new JobOutcome(JobResult.NotAvailable,id);var reward=ledger.Rewards[at];
        if(reward.PortableOnline&&reward.Owner==ledger.Player(state.ActiveProfile))return Collect(id,-1,true);
        if(reward.Owner!=ledger.Player(state.ActiveProfile)||!ledger.PermanentlyRemoved(reward.Giver)||state.Space!=25)return new JobOutcome(JobResult.InvalidContext,id);
        if(state.Visiting)return Collect(id,-1,true);
        bool mailed=false;
        if(!reward.IsReply&&reward.Item!=0){
            if(state.MailCount>=OfflineJobLedger.MailboxCapacity)return new JobOutcome(JobResult.FullMailbox,reward.Job);
            if(!ledger.CanAllocate(1))return new JobOutcome(JobResult.InvalidIdentity,reward.Job);
            ledger.EnsureMail();
            var letter=new JobMailRecord{Id=ledger.Allocate(),Owner=reward.Owner,Sender=reward.Giver,Recipient=reward.Owner,State=JobMailState.Inbox,Reply=true,ReplyText=254,Gift=reward.Item,SentAt=Now,DueAt=Now};
            if(!InsertIncoming(state.ActiveProfile,letter))return new JobOutcome(JobResult.FullMailbox,reward.Job);
            reward.Item=0;ledger.Rewards[at]=reward;mailed=true;
        }
        if(mailed){var paid=Collect(id,-1,true);return new JobOutcome(paid.Result,paid.Job,paid.Amount,JobContent.Letter);}
        return Collect(id,-1,true);
    }
}
