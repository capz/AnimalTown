namespace AnimalTown.Jobs;

// All mutations enter here through a validated command or sequenced host event.
// Views return value snapshots; drawing a menu cannot generate an offer or pay.
public sealed partial class VillagerJobService
{
    private readonly GameState state;
    private OfflineJobLedger ledger=>state.Town.Jobs;
    private JobConversation conversation;
    private ulong nextSession=1;
    private JobIdentity supplySelection;
    private bool openingConversation;
    private readonly IJobRandom? offerOverride,rewardOverride;
    private IJobRandom OfferRandom=>offerOverride??state.Town.JobState.Offers;
    private IJobRandom RewardRandom=>rewardOverride??state.Town.JobState.Rewards;
    public JobConversation Conversation=>conversation;
    public int ConversationNpc=>conversation.Session==0?-1:ledger.FindNpc(conversation.Npc);
    public long Now {get;private set;}
    public IOfflineJobTravel? Travel {get;set;}
    public bool Enabled {get;private set;}
    public VillagerJobService(GameState state,IJobRandom? offerRandom=null,IJobRandom? rewardRandom=null){this.state=state;offerOverride=offerRandom;rewardOverride=rewardRandom;}
    public void Enable(ulong townNonce,long now)
    {
        bool migrating=!ledger.Initialized;JobContent.Validate();state.EnsureJobIdentities(ledger.Initialized?ledger.TownId:townNonce);
        if(migrating){for(int p=0;p<GameState.ProfileCount;p++)for(int n=0;n<6;n++)state.ProfileAt(p).Relationships[n*4+1]=0;for(int n=0;n<6;n++)state.Town.Residents[n].Request=0;}
        Enabled=true;AdvanceTo(now);RecoverBindings();RecoverOpeningParcel();
    }
    public bool BeginConversation(int npc,out JobConversation context)=>StartConversation(npc,false,out context);
    public bool BeginOpeningConversation(int npc)=>state.Tutorial<7&&StartConversation(npc,true,out _);
    private bool StartConversation(int npc,bool opening,out JobConversation context)
    {
        context=default;
        if(!Enabled||(uint)npc>=6||ledger.NpcInTransit(npc)||!InReach(npc))return false;
        EndConversation();if(!opening&&!state.Town.JobState.Resident(npc).CanStartConversation&&!HasActionable(npc))return false;
        if(nextSession==ulong.MaxValue)nextSession=1;
        openingConversation=opening;conversation=new JobConversation(nextSession++,ledger.EnsurePlayer(state.ActiveProfile),ledger.Npc(npc));
        state.MetMask|=1<<npc;state.Town.Residents[npc].Met=1;context=conversation;return true;
    }
    public void EndConversation()
    {
        int npc=ConversationNpc;if(npc>=0&&!openingConversation)state.Town.JobState.Resident(npc).ConversationCompleted();conversation=default;supplySelection=default;openingConversation=false;
    }
    public void InvalidateConversation(){conversation=default;supplySelection=default;openingConversation=false;}
    private bool InReach(int npc)
    {
        var v=state.Town.Residents[npc];if(v.Space!=state.Space)return false;
        float dx=v.PosX-state.X,dz=v.PosZ-state.Z;return dx*dx+dz*dz<=6.25f;
    }
    public ulong NextCommandSequence=>ledger.Initialized?ledger.Receipts[state.ActiveProfile].Sequence+1:1;
    public JobCommand Command(JobCommandKind kind,JobIdentity subject=default,int expectedRevision=-1,int slot=-1)
    {
        var context=conversation.Session!=0?conversation:new JobConversation(0,ledger.Player(state.ActiveProfile),default);
        return new JobCommand(kind,context,NextCommandSequence,subject,expectedRevision,slot);
    }
    public JobOutcome Execute(JobCommand command)
    {
        if(!Enabled||command.Player!=ledger.Player(state.ActiveProfile)||command.Sequence==0)return new JobOutcome(JobResult.InvalidIdentity);
        ref var receipt=ref ledger.Receipts[state.ActiveProfile];
        ulong fingerprint=Fingerprint(command);
        if(command.Sequence==receipt.Sequence)return fingerprint==receipt.Fingerprint?receipt.Outcome:new JobOutcome(JobResult.StaleCommand);
        if(command.Sequence<receipt.Sequence)return new JobOutcome(JobResult.StaleCommand);
        int npc=ledger.FindNpc(command.Npc);
        JobOutcome outcome;
        if(command.Kind!=JobCommandKind.Abandon&&command.Kind!=JobCommandKind.RecoverReward&&(npc<0||conversation.Session==0||command.Session!=conversation.Session||command.Player!=conversation.Player||command.Npc!=conversation.Npc||!InReach(npc)))outcome=new JobOutcome(JobResult.InvalidContext);
        else{
            Reconcile(Now);
            if(command.Kind==JobCommandKind.RecoverReward)outcome=CollectRemovedReward(command.Subject);
            else if(command.Kind==JobCommandKind.Ask)outcome=Ask(npc);
            else if(command.Kind==JobCommandKind.Accept)outcome=Accept(npc,command.Subject,command.ExpectedRevision);
            else if(command.Kind==JobCommandKind.Decline)outcome=Decline(npc,command.Subject,command.ExpectedRevision);
            else if(command.Kind==JobCommandKind.Collect||command.Kind==JobCommandKind.LetterFollowup)outcome=Collect(command.Subject,npc);
            else if(command.Kind==JobCommandKind.Abandon)outcome=Abandon(command.Subject);
            else if(command.Kind==JobCommandKind.SupplyCancel)outcome=CancelSupply(command.Subject,npc);
            else outcome=Complete(command.Subject,npc,command.Slot,command.ExpectedRevision);
        }
        receipt.Sequence=command.Sequence;receipt.Fingerprint=fingerprint;receipt.Outcome=outcome;ledger.Revision++;
        return outcome;
    }
    private static ulong Fingerprint(JobCommand c)
    {
        ulong h=14695981039346656037UL;
        h=unchecked((h^(uint)c.Kind)*1099511628211UL);h=unchecked((h^c.Player.Town)*1099511628211UL);h=unchecked((h^c.Player.Serial)*1099511628211UL);
        h=unchecked((h^c.Npc.Town)*1099511628211UL);h=unchecked((h^c.Npc.Serial)*1099511628211UL);h=unchecked((h^c.Subject.Town)*1099511628211UL);h=unchecked((h^c.Subject.Serial)*1099511628211UL);
        h=unchecked((h^unchecked((uint)c.Slot))*1099511628211UL);return unchecked((h^unchecked((uint)c.ExpectedRevision))*1099511628211UL);
    }
    public void AdvanceTo(long now)
    {
        if(!Enabled)return;
        JobCalendar.Date(now,out int year,out _,out _);if(year<1600||year>2399)throw new ArgumentOutOfRangeException(nameof(now));
        if(ledger.Watermark!=long.MinValue&&now<ledger.Watermark){ShiftTimes(now-ledger.Watermark);ledger.Watermark=now;}
        Now=now;ProcessScheduledMail(now);Reconcile(now);ledger.Watermark=now;
    }
    private void ShiftTimes(long delta)
    {
        for(int i=0;i<ledger.Jobs.Length;i++)if(ledger.Jobs[i].Occupying){ref var j=ref ledger.Jobs[i];j.AcceptedAt+=delta;j.StageAt+=delta;j.OriginalDueAt+=delta;j.DueAt+=delta;if(j.CompletedAt!=0)j.CompletedAt+=delta;if(j.RetainedUntil!=0)j.RetainedUntil+=delta;if(j.EvidenceAt!=0)j.EvidenceAt+=delta;}
        for(int i=0;i<ledger.Offers.Length;i++)if(!ledger.Offers[i].Id.IsEmpty)ledger.Offers[i].CreatedAt+=delta;
        for(int i=0;i<ledger.Rewards.Length;i++)if(ledger.Rewards[i].State==JobRewardState.Owed)ledger.Rewards[i].ResolvedAt+=delta;
        for(int i=0;i<ledger.World.Length;i++)if(ledger.World[i].Kind!=JobWorldKind.Empty)ledger.World[i].CreatedAt+=delta;
        ShiftScheduledMail(delta);
    }
    private void Reconcile(long now)
    {
        for(int i=0;i<ledger.Jobs.Length;i++){
            var j=ledger.Jobs[i];if(!j.Occupying||j.Status==JobStatus.RewardOwed)continue;
            if(j.Template==JobTemplate.FavorFlowers&&j.Status!=JobStatus.CompletedRetained){int npc=ledger.FindNpc(j.Requester);if(npc<0||j.HomeAcre!=Acre(npc)){CancelJob(i);continue;}}
            bool season=!JobCalendar.Season(j.Template,now);
            if(j.Status==JobStatus.CompletedRetained){if(now>=j.RetainedUntil||season){ledger.Jobs[i]=default;ledger.Revision++;}continue;}
            if(j.Status==JobStatus.ReplyOwed){if(now>j.OriginalDueAt){ledger.Jobs[i]=default;ledger.Revision++;}continue;}
            if(now>j.DueAt||season){int penalty=0;
                if(!season&&!j.Shared)penalty=j.Template==JobTemplate.RetrievalBorrowed?(j.Status==JobStatus.Returning?-2:0):j.Template==JobTemplate.DeliveryForeign||j.Template==JobTemplate.DeliveryMoved?-1:-5;
                if(penalty!=0)Friendship(j.Owner,j.Requester,penalty);CancelJob(i,season?JobEndReason.SeasonClosed:JobEndReason.Expired);}
        }
    }
    private void Friendship(JobIdentity owner,JobIdentity npcId,int delta)
    {
        int profile=ledger.FindPlayer(owner);if(profile<0)return;int npc=ledger.FindNpc(npcId);
        int rel=ledger.RelationSlot(profile,npcId,true);if(rel<0)return;
        int current=npc>=0?(profile==state.ActiveProfile?state.Town.Residents[npc].Friendship:state.ProfileAt(profile).Relationships[npc*4]):ledger.Relations[rel].Friendship;
        int value=Math.Max(0,Math.Min(255,current+delta));ledger.Relations[rel].Friendship=value;
        if(npc>=0){state.ProfileAt(profile).Relationships[npc*4]=value;if(profile==state.ActiveProfile)state.Town.Residents[npc].Friendship=value;}
    }
    private void CancelJob(int at,JobEndReason reason=JobEndReason.WorldRemoved)
    {
        RecordEnd(ledger.Jobs[at],reason);ledger.Revision++;
        var j=ledger.Jobs[at];if(!j.Binding.IsEmpty)RemoveBinding(j.Binding);
        int reward=ledger.FindReward(j.Obligation);if(reward>=0&&ledger.Rewards[reward].State==JobRewardState.Reserved)ledger.Rewards[reward]=default;
        ledger.Jobs[at]=default;
    }
    private void RemoveBinding(JobIdentity binding)
    {
        for(int p=0;p<OfflineJobLedger.OwnerCount;p++)for(int s=0;s<GameState.MaximumPockets;s++)if(ledger.Binding(p,s)==binding){ledger.SetBinding(p,s,default);state.ProfileAt(p).Pockets[s]=0;if(p==state.ActiveProfile)state.Pockets[s]=0;}
    }
    internal void RemoveProfileData(int profile)
    {
        if(!ledger.Initialized)return;var owner=ledger.Player(profile);if(owner.IsEmpty)return;
        for(int i=0;i<ledger.Jobs.Length;i++){var j=ledger.Jobs[i];if(j.Occupying&&(j.Owner==owner||j.Claimant==owner&&j.Status!=JobStatus.CompletedRetained))CancelJob(i,JobEndReason.ProfileDeleted);}
        for(int i=0;i<ledger.Rewards.Length;i++)if(ledger.Rewards[i].Owner==owner)ledger.Rewards[i]=default;
        for(int i=0;i<ledger.Mail.Length;i++){
            var m=ledger.Mail[i];if(m.State==JobMailState.Empty)continue;
            if(m.Owner==owner){ledger.Mail.Set(i,default);continue;}
            if(m.State==JobMailState.Queued&&m.Recipient==owner){m.Recipient=m.Sender;ledger.Mail.Set(i,m);}
        }
        for(int i=0;i<ledger.Offers.Length;i++)if(ledger.Offers[i].Owner==owner)ledger.Offers[i]=default;
        for(int i=0;i<GameState.MaximumPockets;i++)ledger.SetBinding(profile,i,default);
        for(int i=0;i<OfflineJobLedger.RelationsPerPlayer;i++)ledger.Relations[profile*OfflineJobLedger.RelationsPerPlayer+i]=default;
        for(int i=0;i<ledger.Terminals.Length;i++)if(ledger.Terminals[i].Owner==owner)ledger.Terminals[i]=default;
        ledger.OpeningParcels[profile]=default;ledger.HomeFloors[profile]=0;ledger.HomeWalls[profile]=0;ledger.Receipts[profile]=default;ledger.ForeignNpcs[profile]=default;ledger.ForeignTowns[profile]=0;InvalidateConversation();
    }
    public void RecoverBindings()
    {
        if(!ledger.Initialized)return;
        for(int i=0;i<ledger.Jobs.Length;i++)if(ledger.Jobs[i].Occupying&&!ledger.Jobs[i].Binding.IsEmpty){
            var j=ledger.Jobs[i];int owner=ledger.FindPlayer(j.Owner),matches=0,references=0;
            for(int p=0;p<OfflineJobLedger.OwnerCount;p++)for(int s=0;s<GameState.MaximumPockets;s++)if(ledger.Binding(p,s)==j.Binding){references++;int item=p==state.ActiveProfile?state.Pockets[s]:state.ProfileAt(p).Pockets[s];if(p==owner&&s<(state.ProfileAt(p).BackpackTier+1)*10&&item==JobContent.Parcel)matches++;}
            if(matches!=1||references!=1)CancelJob(i,JobEndReason.BrokenBinding);
        }
        for(int p=0;p<OfflineJobLedger.OwnerCount;p++)for(int s=0;s<GameState.MaximumPockets;s++)if(ledger.HasBinding(p,s)){
            var binding=ledger.Binding(p,s);bool found=false;for(int i=0;i<ledger.Jobs.Length;i++)if(ledger.Jobs[i].Occupying&&ledger.Jobs[i].Binding==binding){found=true;break;}
            if(!found&&ledger.OpeningParcels[p]==binding)found=true;
            if(!found){ledger.SetBinding(p,s,default);if(p==state.ActiveProfile)state.Pockets[s]=0;state.ProfileAt(p).Pockets[s]=0;}
        }
    }
    private JobOutcome Abandon(JobIdentity id)
    {int at=ledger.FindJob(id);if(at<0)return new JobOutcome(JobResult.NotAvailable);var j=ledger.Jobs[at];if(j.Shared||j.Owner!=ledger.Player(state.ActiveProfile)||j.Status==JobStatus.RewardOwed)return new JobOutcome(JobResult.NotAvailable);Friendship(j.Owner,j.Requester,-3);CancelJob(at,JobEndReason.Abandoned);return new JobOutcome(JobResult.Ok,id);}
    private JobOutcome CancelSupply(JobIdentity id,int npc)
    {int at=ledger.FindJob(id);if(at<0||supplySelection!=id)return new JobOutcome(JobResult.NotAvailable);var j=ledger.Jobs[at];supplySelection=default;if(j.Requester!=ledger.Npc(npc)||j.Template!=JobTemplate.FavorFish&&j.Template!=JobTemplate.FavorInsect)return new JobOutcome(JobResult.NotAvailable);Friendship(ledger.Player(state.ActiveProfile),j.Requester,-1);return new JobOutcome(JobResult.Ok,id);}
    public bool BeginSupplySelection(JobIdentity id)
    {int at=ledger.FindJob(id),npc=ConversationNpc;if(at<0||npc<0||!InReach(npc))return false;var j=ledger.Jobs[at];if(j.Status!=JobStatus.Active||j.Requester!=ledger.Npc(npc)||j.Template!=JobTemplate.FavorFruit&&j.Template!=JobTemplate.FavorFish&&j.Template!=JobTemplate.FavorInsect)return false;if(!j.Claimant.IsEmpty&&j.Claimant!=ledger.Player(state.ActiveProfile))return false;supplySelection=id;return true;}
    private bool HasActionable(int npc)
    {var best=BestInteraction(npc);if(best.Result!=JobResult.ExistingJob)return false;if(ledger.FindReward(best.Job)>=0)return true;int at=ledger.FindJob(best.Job);if(at<0)return false;var j=ledger.Jobs[at];return j.Target==ledger.Npc(npc)||j.Shared&&j.Status!=JobStatus.CompletedRetained;}
    // Implementations live beside their adapters, not in presentation code.
    partial void ProcessScheduledMail(long now);
    partial void ShiftScheduledMail(long delta);
}
