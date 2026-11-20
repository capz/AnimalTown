namespace AnimalTown.Jobs;

public sealed partial class VillagerJobService
{
    private JobOutcome Complete(JobIdentity id,int npc,int pocket,int revision)
    {
        int at=ledger.FindJob(id);if(at<0)return new JobOutcome(JobResult.NotAvailable,id);
        var j=ledger.Jobs[at];JobIdentity player=ledger.Player(state.ActiveProfile),who=ledger.Npc(npc);
        if(revision>=0&&revision!=j.Revision)return new JobOutcome(JobResult.StaleCommand,id);
        if(!j.Shared&&j.Owner!=player)return new JobOutcome(JobResult.InvalidIdentity,id);
        if(!j.Claimant.IsEmpty&&j.Claimant!=player)return new JobOutcome(JobResult.AlreadyClaimed,id);
        if(j.Status==JobStatus.CompletedRetained)return new JobOutcome(JobResult.Retained,id);
        if(j.Status==JobStatus.RewardOwed||j.Status==JobStatus.ReplyOwed)return Collect(j.Obligation,npc);
        if(j.Template==JobTemplate.RetrievalBorrowed&&j.Status==JobStatus.Seeking)return Retrieval(at,npc);
        if(!j.Shared){
            if(j.Target!=who)return new JobOutcome(JobResult.WrongNpc,id);
            int bound=-1;for(int s=0;s<state.Capacity;s++)if(ledger.Binding(state.ActiveProfile,s)==j.Binding&&state.Pockets[s]==JobContent.Parcel){bound=s;break;}
            if(bound<0||j.Binding.IsEmpty||pocket>=0&&pocket!=bound)return new JobOutcome(JobResult.WrongItem,id);
            int oldOutfit=ledger.Outfit(npc);var frozen=ResolveReward(j,player,who,oldOutfit);
            state.Pockets[bound]=0;ledger.SetBinding(state.ActiveProfile,bound,default);j.Binding=default;
            if(j.Template==JobTemplate.DeliveryClothing)ledger.SetOutfit(npc,j.Item);
            return CommitSuccess(at,j,frozen,player,who);
        }
        if(j.Requester!=who)return new JobOutcome(JobResult.WrongNpc,id);
        if(j.Template==JobTemplate.FavorLetter)return new JobOutcome(JobResult.NotReady,id);
        if(j.Template==JobTemplate.FavorFruit||j.Template==JobTemplate.FavorFish||j.Template==JobTemplate.FavorInsect){
            if((uint)pocket>=state.Capacity||state.IsJobItem(pocket))return new JobOutcome(JobResult.WrongItem,id);
            int item=state.Pockets[pocket];bool eligible=j.Template==JobTemplate.FavorFruit?item==j.Item:j.Template==JobTemplate.FavorFish?Catalog.Has(item,ItemFlags.Fish):Catalog.Has(item,ItemFlags.Insect);
            if(!eligible)return new JobOutcome(JobResult.WrongItem,id);
            if(j.Template!=JobTemplate.FavorFruit&&supplySelection!=id)return new JobOutcome(JobResult.InvalidContext,id);
            var frozen=ResolveReward(j,player,who,0);state.Pockets[pocket]=0;supplySelection=default;return CommitSuccess(at,j,frozen,player,who);
        }
        if(j.Template==JobTemplate.FavorFlowers){
            if(j.HomeAcre!=Acre(npc)){CancelJob(at);return new JobOutcome(JobResult.NotAvailable,id);}
            FlowerCounts(npc,out int total,out _,out _);if(total<j.FlowerBaseline+4)return new JobOutcome(JobResult.NotReady,id);
        }else if(j.Status!=JobStatus.ReadyToReport||j.Evidence.IsEmpty)return new JobOutcome(JobResult.NotReady,id);
        return CommitSuccess(at,j,ResolveReward(j,player,who,0),player,who);
    }
    private JobOutcome Retrieval(int at,int npc)
    {
        var j=ledger.Jobs[at];if(j.Target!=ledger.Npc(npc))return new JobOutcome(JobResult.WrongNpc,j.Id);
        if(j.ChainDecision==0){
            bool continueChain=j.Borrowers<4&&OfferRandom.NextInt(2)==JobRulesV1.ContinueOutcome;
            var next=continueChain?PickTarget(npc,j):default;j.ChainDecision=next.IsEmpty?2:1;j.NextBorrower=next;j.Revision++;ledger.Jobs[at]=j;
        }
        if(j.ChainDecision==1){j.Target=j.NextBorrower;j.SetBorrower(j.Borrowers++,j.Target);j.NextBorrower=default;j.ChainDecision=0;j.StageAt=Now;j.DueAt=Now+2*86400;j.Revision++;ledger.Jobs[at]=j;return new JobOutcome(JobResult.ExistingJob,j.Id);}
        int pocket=state.FreeSlot();if(pocket<0)return new JobOutcome(JobResult.FullPockets,j.Id);
        if(!ledger.CanAllocate(1))return new JobOutcome(JobResult.InvalidIdentity,j.Id);
        j.Binding=ledger.Allocate();state.Pockets[pocket]=JobContent.Parcel;ledger.SetBinding(state.ActiveProfile,pocket,j.Binding);
        j.Target=j.Requester;j.Status=JobStatus.Returning;j.StageAt=Now;j.DueAt=Now+2*86400;j.Revision++;ledger.Jobs[at]=j;return new JobOutcome(JobResult.Ok,j.Id);
    }
    private VillagerJobReward ResolveReward(VillagerJobRecord job,JobIdentity player,JobIdentity giver,int oldOutfit)
    {
        int at=ledger.FindReward(job.Obligation);if(at<0)throw new InvalidOperationException("Accepted job has no reward reservation");
        var reward=ledger.Rewards[at];if(reward.State==JobRewardState.Owed)return reward;
        var category=JobSelection.RollReward(RewardRandom,job.Template,job.Borrowers);
        reward.State=JobRewardState.Owed;reward.Owner=player;reward.Giver=giver;reward.ResolvedAt=Now;
        if(category==JobRewardCategory.Currency)reward.Currency=JobSelection.RollMoney(RewardRandom,JobRulesV1.MoneyBase(job.Template,job.Borrowers));
        else if(category==JobRewardCategory.WornClothing&&JobContent.Matches(oldOutfit,JobRewardCategory.Clothing))reward.Item=oldOutfit;
        else{
            int candidate=0;if(category==JobRewardCategory.Furniture&&RewardRandom.NextInt(10)==0)candidate=HouseCandidate(ledger.FindNpc(giver));
            reward.Item=candidate!=0?candidate:JobContent.PickCategory(RewardRandom,category==JobRewardCategory.WornClothing?JobRewardCategory.Clothing:category,category==JobRewardCategory.WornClothing?state.Shirt:0);
        }
        return reward;
    }
    private int HouseCandidate(int npc)
    {
        if(npc<0)return 0;int count=0,version=17;
        for(int i=0;i<state.Town.PlacedCount;i++){var p=state.Town.Placed[i];if(p.Room==16+npc&&p.Owner==npc+1&&JobContent.Matches(p.Item,JobRewardCategory.Furniture)){count++;version=unchecked(version*31+p.Item*17+p.X*3+p.Z*7+p.Rotation);}}
        if(ledger.FurnitureCacheVersions[npc]==version)return ledger.FurnitureCacheItems[npc];
        int selected=0;if(count>0){int draw=state.Town.JobState.HouseCache.NextInt(count);for(int i=0;i<state.Town.PlacedCount;i++){var p=state.Town.Placed[i];if(p.Room==16+npc&&p.Owner==npc+1&&JobContent.Matches(p.Item,JobRewardCategory.Furniture)&&draw--==0){selected=p.Item;break;}}}
        ledger.FurnitureCacheVersions[npc]=version;ledger.FurnitureCacheItems[npc]=selected;return selected;
    }
    private JobOutcome CommitSuccess(int at,VillagerJobRecord j,VillagerJobReward reward,JobIdentity claimant,JobIdentity giver)
    {
        int rewardSlot=ledger.FindReward(j.Obligation);ledger.Rewards[rewardSlot]=reward;j.Claimant=claimant;j.CompletedAt=Now;j.Revision++;
        if(j.Shared){j.Status=JobStatus.CompletedRetained;j.RetainedUntil=j.OriginalDueAt+3*86400;}else j.Status=JobStatus.RewardOwed;
        ledger.Jobs[at]=j;Friendship(claimant,giver,3);return Collect(reward.Id,ledger.FindNpc(giver));
    }
    private JobOutcome Collect(JobIdentity id,int npc)
    {
        int at=ledger.FindReward(id);if(at<0){int job=ledger.FindJob(id);if(job>=0)at=ledger.FindReward(ledger.Jobs[job].Obligation);}
        if(at<0)return new JobOutcome(JobResult.NotAvailable,id);var reward=ledger.Rewards[at];
        if(reward.State!=JobRewardState.Owed)return new JobOutcome(JobResult.NotReady,id);
        if(reward.Owner!=ledger.Player(state.ActiveProfile))return new JobOutcome(JobResult.AlreadyClaimed,id);
        if(npc<0||reward.Giver!=ledger.Npc(npc))return new JobOutcome(JobResult.WrongNpc,id);
        if(reward.IsReply)return CollectReply(at);
        int credited=Math.Min(reward.Currency,GameState.WalletLimit-state.Wallet),item=0;
        if(reward.Item!=0){if(state.FreeSlot()<0)return new JobOutcome(JobResult.FullPockets,reward.Job,item:reward.Item);state.Receive(reward.Item);item=reward.Item;reward.Item=0;}
        state.Wallet+=credited;reward.Currency-=credited;
        if(reward.Currency==0&&reward.Item==0){ledger.Rewards[at]=default;int job=ledger.FindJob(reward.Job);if(job>=0&&!ledger.Jobs[job].Shared)ledger.Jobs[job]=default;}
        else ledger.Rewards[at]=reward;
        return new JobOutcome(credited==0&&reward.Currency>0?JobResult.NotReady:JobResult.Ok,reward.Job,credited,item);
    }
    public bool RecordWorldEvidence(JobTemplate template,JobIdentity objectId,JobIdentity creditedPlayer,long at,ulong sourceSequence,JobIdentity contactedNpc=default,int homeAcre=-1,bool destroyed=false)
    {
        if(!Enabled||sourceSequence<=ledger.EventSequences[1]||at<ledger.Watermark)return false;
        ledger.EventSequences[1]=sourceSequence;if(at>Now)AdvanceTo(at);
        for(int i=OfflineJobLedger.SharedStart;i<ledger.Jobs.Length;i++){
            var j=ledger.Jobs[i];if(!j.Occupying||j.Template!=template||j.Status==JobStatus.CompletedRetained||j.Status==JobStatus.ReplyOwed)continue;
            if(destroyed){if(template==JobTemplate.FavorSnowman&&j.Evidence==objectId&&j.Status==JobStatus.ReadyToReport){j.Evidence=default;j.Claimant=default;j.EvidenceAt=0;j.Status=JobStatus.Active;j.Revision++;ledger.Jobs[i]=j;return true;}continue;}
            if(j.Status!=JobStatus.Active||at<j.AcceptedAt||at>j.DueAt||!objectId.IsValid||ledger.FindPlayer(creditedPlayer)<0)continue;
            if(template==JobTemplate.FavorBall&&j.Requester!=contactedNpc)continue;
            if(template==JobTemplate.FavorSnowman){int requester=ledger.FindNpc(j.Requester);if(requester<0||Acre(requester)!=homeAcre)continue;}
            j.Evidence=objectId;j.Claimant=creditedPlayer;j.EvidenceAt=at;j.Status=JobStatus.ReadyToReport;j.Revision++;ledger.Jobs[i]=j;ledger.Revision++;return true;
        }
        return false;
    }
}
