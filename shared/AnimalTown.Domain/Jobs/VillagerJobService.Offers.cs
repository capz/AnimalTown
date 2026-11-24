namespace AnimalTown.Jobs;

public sealed partial class VillagerJobService
{
    public JobOutcome BestInteraction(int npc)
    {
        if(!ledger.Initialized||(uint)npc>=6)return new JobOutcome(JobResult.NoWork);
        var player=ledger.Player(state.ActiveProfile);var who=ledger.Npc(npc);int best=-1;
        for(int i=0;i<ledger.Rewards.Length;i++){var r=ledger.Rewards[i];if(r.State!=JobRewardState.Owed&&(r.State!=JobRewardState.Custody||r.Currency==0)||r.Owner!=player||r.Giver!=who||r.AutomaticReply&&Now<r.ReplyAt)continue;
            if(best<0||r.ResolvedAt<ledger.Rewards[best].ResolvedAt||r.ResolvedAt==ledger.Rewards[best].ResolvedAt&&JobIdentity.Compare(r.Id,ledger.Rewards[best].Id)<0)best=i;}
        if(best>=0)return new JobOutcome(JobResult.ExistingJob,ledger.Rewards[best].Id);
        int priority=99;best=-1;
        for(int i=0;i<ledger.Jobs.Length;i++){
            var j=ledger.Jobs[i];if(!j.Occupying||!j.Shared&&j.Owner!=player)continue;
            int p=99;
            if(!j.Shared&&j.Target==who)p=j.Status==JobStatus.Seeking?3:2;
            if(!j.Shared&&j.Requester==who)p=Math.Min(p,4);
            if(j.Shared&&j.Requester==who)p=j.Status==JobStatus.CompletedRetained?6:5;
            if(p<priority||p==priority&&p<99&&(best<0||j.AcceptedAt<ledger.Jobs[best].AcceptedAt||j.AcceptedAt==ledger.Jobs[best].AcceptedAt&&JobIdentity.Compare(j.Id,ledger.Jobs[best].Id)<0)){best=i;priority=p;}
        }
        if(best>=0){var j=ledger.Jobs[best];return new JobOutcome(j.Status==JobStatus.CompletedRetained?JobResult.Retained:!j.Claimant.IsEmpty&&j.Claimant!=player?JobResult.AlreadyClaimed:JobResult.ExistingJob,j.Id);}
        var offer=ledger.Offer(state.ActiveProfile,npc);return offer.Id.IsEmpty?new JobOutcome(JobResult.NoWork):new JobOutcome(JobResult.ExistingOffer,offer.Id);
    }
    private JobOutcome Ask(int npc)
    {
        var best=BestInteraction(npc);
        if(best.Result==JobResult.ExistingOffer){var o=ledger.Offer(state.ActiveProfile,npc);if(!OfferValid(o,npc)){InvalidateOffer(npc);return new JobOutcome(JobResult.OfferInvalidated);}return best;}
        if(best.Result!=JobResult.NoWork)return best;
        var availability=state.Town.JobState.Resident(npc);
        if(!state.AllowsFreshResidentJobs(npc)||!availability.CanStartConversation||!availability.FreshGenerationEnabled)return new JobOutcome(JobResult.NoWork);
        int freeOffer=ledger.OfferSlot(state.ActiveProfile,npc,true);if(freeOffer<0)return new JobOutcome(JobResult.FullOffers);
        if(!ledger.CanAllocate(1))return new JobOutcome(JobResult.InvalidIdentity);
        if(!JobSelection.RollTemplate(OfferRandom,out var template)){availability.StartNoJobCooldown();return new JobOutcome(JobResult.NoWork);}
        bool prerequisites=JobCalendar.Season(template,Now,true)&&(!state.Visiting||template!=JobTemplate.FavorLetter);
        if(template>=JobTemplate.FavorFruit)prerequisites&=!FavorOccupied(template)&&!ledger.Jobs[OfflineJobLedger.SharedStart+npc].Occupying;
        if(template==JobTemplate.FavorFlowers){FlowerCounts(npc,out _,out int mature,out int empty);prerequisites&=mature<=20&&empty>=4;}
        if(template==JobTemplate.DeliveryForeign)prerequisites&=RemoteValid(template,ledger.ForeignNpcs[state.ActiveProfile])&&!HasRemoteDuplicate(template);
        if(template==JobTemplate.DeliveryMoved)prerequisites&=RemoteValid(template,ledger.LastMovedNpc)&&!HasRemoteDuplicate(template);
        if(template==JobTemplate.DeliveryClothing||template==JobTemplate.DeliveryProperty||template==JobTemplate.RetrievalBorrowed){bool target=false;for(int i=0;i<TownJobState.ResidentCount;i++)target|=EligibleTarget(i,npc,default);prerequisites&=target;}
        if(!prerequisites){availability.StartNoJobCooldown();return new JobOutcome(JobResult.NoWork);}
        var oNew=new VillagerJobOffer{Owner=ledger.Player(state.ActiveProfile),Requester=ledger.Npc(npc),Template=template,CreatedAt=Now,Revision=1};
        bool valid=JobCalendar.Season(template,Now,true);
        if(template==JobTemplate.DeliveryClothing){int exclude=ledger.Outfit(npc);oNew.Item=JobContent.PickCategory(OfferRandom,JobRewardCategory.Clothing,exclude);oNew.Target=PickTarget(npc,default);valid&=oNew.Target.IsValid;}
        else if(template==JobTemplate.DeliveryProperty||template==JobTemplate.RetrievalBorrowed){oNew.Item=JobContent.PickTagged(OfferRandom,ItemFlags.Property);oNew.Target=PickTarget(npc,default);valid&=oNew.Target.IsValid;}
        else if(template==JobTemplate.DeliveryForeign){oNew.Target=ledger.ForeignNpcs[state.ActiveProfile];oNew.Item=JobContent.PickTagged(OfferRandom,ItemFlags.Property);valid&=RemoteValid(template,oNew.Target)&&!HasRemoteDuplicate(template);}
        else if(template==JobTemplate.DeliveryMoved){oNew.Target=ledger.LastMovedNpc;oNew.Item=JobContent.PickTagged(OfferRandom,ItemFlags.Property);valid&=RemoteValid(template,oNew.Target)&&!HasRemoteDuplicate(template);}
        else if(template==JobTemplate.FavorFruit)oNew.Item=JobContent.PickTagged(OfferRandom,ItemFlags.Fruit,ledger.NativeFruit);
        if(template>=JobTemplate.FavorFruit)valid&=!FavorOccupied(template)&&!ledger.Jobs[OfflineJobLedger.SharedStart+npc].Occupying;
        if(!valid){availability.StartNoJobCooldown();return new JobOutcome(JobResult.NoWork);}
        oNew.Id=ledger.Allocate();ledger.Offers[freeOffer]=oNew;return new JobOutcome(JobResult.ExistingOffer,oNew.Id);
    }
    private bool HasRemoteDuplicate(JobTemplate template,JobIdentity exceptOffer=default)
    {
        var player=ledger.Player(state.ActiveProfile);
        for(int i=0;i<ledger.Jobs.Length;i++)if(ledger.Jobs[i].Occupying&&ledger.Jobs[i].Owner==player&&ledger.Jobs[i].Template==template)return true;
        for(int i=0;i<ledger.Offers.Length;i++)if(!ledger.Offers[i].Id.IsEmpty&&ledger.Offers[i].Id!=exceptOffer&&ledger.Offers[i].Owner==player&&ledger.Offers[i].Template==template)return true;
        return false;
    }
    private bool RemoteValid(JobTemplate template,JobIdentity target)
    {
        ulong town=template==JobTemplate.DeliveryMoved?ledger.LastMovedTown:ledger.ForeignTowns[state.ActiveProfile];
        var remembered=template==JobTemplate.DeliveryMoved?ledger.LastMovedNpc:ledger.ForeignNpcs[state.ActiveProfile];
        return target.IsValid&&target==remembered&&town!=0&&town!=ledger.TownId&&Travel!=null&&Travel.Reachable(town,target);
    }
    private bool OfferValid(VillagerJobOffer o,int npc)
    {
        if(o.Owner!=ledger.Player(state.ActiveProfile)||o.Requester!=ledger.Npc(npc)||!JobCalendar.Season(o.Template,Now,true))return false;
        if(o.Template==JobTemplate.DeliveryForeign||o.Template==JobTemplate.DeliveryMoved)return RemoteValid(o.Template,o.Target)&&!HasRemoteDuplicate(o.Template,o.Id);
        if(o.Template==JobTemplate.DeliveryClothing||o.Template==JobTemplate.DeliveryProperty||o.Template==JobTemplate.RetrievalBorrowed){int target=ledger.FindNpc(o.Target);if(target<0||target==npc||Acre(target)==Acre(npc))return false;if(o.Template==JobTemplate.DeliveryClothing)return o.Item!=ledger.Outfit(npc)&&JobContent.Matches(o.Item,JobRewardCategory.Clothing);return Catalog.Has(o.Item,ItemFlags.Property);}
        if(FavorOccupied(o.Template)||ledger.Jobs[OfflineJobLedger.SharedStart+npc].Occupying)return false;
        if(o.Template==JobTemplate.FavorFruit)return o.Item!=ledger.NativeFruit&&Catalog.Has(o.Item,ItemFlags.Fruit);
        if(o.Template==JobTemplate.FavorFlowers){FlowerCounts(npc,out _,out int mature,out int empty);return mature<=20&&empty>=4;}
        return true;
    }
    private bool FavorOccupied(JobTemplate template)
    {for(int i=OfflineJobLedger.SharedStart;i<ledger.Jobs.Length;i++)if(ledger.Jobs[i].Occupying&&ledger.Jobs[i].Template==template)return true;return false;}
    private void InvalidateOffer(int npc)
    {int at=ledger.OfferSlot(state.ActiveProfile,npc,false);if(at>=0)ledger.Offers[at]=default;state.Town.JobState.Resident(npc).StartNoJobCooldown();}
    private JobOutcome Accept(int npc,JobIdentity id,int revision)
    {
        int offerSlot=ledger.OfferSlot(state.ActiveProfile,npc,false);if(offerSlot<0)return new JobOutcome(JobResult.NoOffer);var offer=ledger.Offers[offerSlot];
        if(offer.Id.IsEmpty||offer.Id!=id)return new JobOutcome(JobResult.NoOffer);
        if(revision>=0&&revision!=offer.Revision)return new JobOutcome(JobResult.StaleCommand,id);
        if(!state.Town.JobState.Resident(npc).CanStartConversation)return new JobOutcome(JobResult.NotAvailable,id);
        if(!OfferValid(offer,npc)){InvalidateOffer(npc);return new JobOutcome(JobResult.OfferInvalidated,id);}
        var definition=JobRulesV1.Definition(offer.Template);
        int slot=definition.Family==JobFamily.Favor?OfflineJobLedger.SharedStart+npc:ledger.FreePersonal(state.ActiveProfile,definition.Family);
        if(slot<0)return new JobOutcome(JobResult.FullJobs,id);
        int reward=ledger.FreeReward();if(reward<0)return new JobOutcome(JobResult.FullRewards,id);
        if(!ledger.CanAllocate(definition.ParcelOnAcceptance?3:2))return new JobOutcome(JobResult.InvalidIdentity,id);
        int pocket=definition.ParcelOnAcceptance?state.FreeSlot():-1;if(definition.ParcelOnAcceptance&&pocket<0)return new JobOutcome(JobResult.FullPockets,id);
        var j=new VillagerJobRecord{Id=ledger.Allocate(),Owner=definition.Family==JobFamily.Favor?default:offer.Owner,AcceptedBy=offer.Owner,Requester=offer.Requester,Target=offer.Target,
            Template=offer.Template,Status=offer.Template==JobTemplate.RetrievalBorrowed?JobStatus.Seeking:JobStatus.Active,Item=offer.Item,AcceptedAt=Now,StageAt=Now,OriginalDueAt=Now+(long)definition.DeadlineDays*86400,DueAt=Now+(long)definition.DeadlineDays*86400,Revision=1};
        j.Obligation=ledger.Allocate();
        if(offer.Template==JobTemplate.RetrievalBorrowed){j.Borrowers=1;j.Borrower1=j.Target;}
        if(offer.Template==JobTemplate.FavorFlowers){FlowerCounts(npc,out int total,out _,out _);j.FlowerBaseline=total;j.HomeAcre=Acre(npc);}
        if(definition.ParcelOnAcceptance){j.Binding=ledger.Allocate();state.Pockets[pocket]=JobContent.Parcel;ledger.SetBinding(state.ActiveProfile,pocket,j.Binding);}
        ledger.Rewards[reward]=new VillagerJobReward{Id=j.Obligation,Job=j.Id,Owner=j.Owner,Giver=j.Requester,State=JobRewardState.Reserved};
        ledger.Jobs[slot]=j;ledger.Offers[offerSlot]=default;return new JobOutcome(JobResult.Ok,j.Id);
    }
    private JobOutcome Decline(int npc,JobIdentity id,int revision)
    {
        int at=ledger.OfferSlot(state.ActiveProfile,npc,false);if(at<0)return new JobOutcome(JobResult.NoOffer);var offer=ledger.Offers[at];if(offer.Id.IsEmpty||offer.Id!=id)return new JobOutcome(JobResult.NoOffer);
        if(revision>=0&&revision!=offer.Revision)return new JobOutcome(JobResult.StaleCommand,id);
        if(!offer.Refused){Friendship(offer.Owner,offer.Requester,-3);offer.Refused=true;offer.Revision++;ledger.Offers[at]=offer;}
        return new JobOutcome(JobResult.Ok,id);
    }
    private int Acre(int npc)
    {var v=state.Town.Residents[npc];return v.HomeX/Town.ScreenTileSize+v.HomeZ/Town.ScreenTileSize*Town.ScreenColumns;}
    private bool EligibleTarget(int candidate,int sender,VillagerJobRecord chain)
    {
        var id=ledger.Npc(candidate);if(!id.IsValid||candidate==sender||!state.ResidentPresent(candidate)||!state.AllowsFreshResidentJobs(candidate)||Acre(candidate)==Acre(sender)||id==chain.Requester)return false;
        for(int i=0;i<chain.Borrowers;i++)if(chain.Borrower(i)==id)return false;return true;
    }
    private JobIdentity PickTarget(int sender,VillagerJobRecord chain)
    {
        // Rank only fresh destinations. Preserve existing offers and select
        // among equal candidates in stable identity order with one RNG draw.
        var candidates=new JobIdentity[TownJobState.ResidentCount];
        int count=0,quietest=int.MaxValue;
        for(int i=0;i<candidates.Length;i++){
            if(!EligibleTarget(i,sender,chain))continue;
            var destination=state.Town.NpcAt(i);
            int nearby=VillagerNavigation.NearbyActors(state,i,destination.Space,destination.PosX,destination.PosZ,VillagerNavigation.CrowdRadius);
            if(nearby>quietest)continue;
            if(nearby<quietest){quietest=nearby;count=0;}
            var id=ledger.Npc(i);
            int at=count++;
            while(at>0&&JobIdentity.Compare(candidates[at-1],id)>0){candidates[at]=candidates[at-1];at--;}
            candidates[at]=id;
        }
        return count==0?default:candidates[OfferRandom.NextInt(count)];
    }
    public void FlowerCounts(int npc,out int total,out int mature,out int empty)
    {
        total=0;mature=0;empty=0;int acre=Acre(npc),left=acre%Town.ScreenColumns*20,top=acre/Town.ScreenColumns*20;
        for(int i=0;i<state.Town.GroundCount;i++){var g=state.Town.Ground[i];if(g.Item==2&&g.Kind==2&&g.X>=left&&g.X<left+20&&g.Z>=top&&g.Z<top+20){total++;mature++;}}
        for(int z=top;z<Math.Min(Town.Height,top+20);z++)for(int x=left;x<Math.Min(Town.Width,left+20);x++)if(state.Town.At(x,z)==(int)Terrain.Grass&&state.Town.FindGround(x,z)<0&&state.CanWalk(x+.5f,z+.5f,0))empty++;
    }
}
