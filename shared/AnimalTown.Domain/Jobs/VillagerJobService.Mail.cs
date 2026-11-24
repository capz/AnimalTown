namespace AnimalTown.Jobs;

public sealed partial class VillagerJobService
{
    private static ulong MailFingerprint(JobCommandKind kind,JobIdentity target,int first,int second,string text)
    {
        ulong h=unchecked(14695981039346656037UL^(uint)kind);h=unchecked((h^target.Town)*1099511628211UL);h=unchecked((h^target.Serial)*1099511628211UL);h=unchecked((h^unchecked((uint)first))*1099511628211UL);h=unchecked((h^unchecked((uint)second))*1099511628211UL);
        for(int i=0;i<text.Length;i++)h=unchecked((h^text[i])*1099511628211UL);return h;
    }
    private bool MailReceipt(ulong sequence,ulong fingerprint,out JobOutcome outcome)
    {
        var receipt=ledger.Receipts[state.ActiveProfile];outcome=default;
        if(sequence==0||sequence<receipt.Sequence){outcome=new JobOutcome(JobResult.StaleCommand);return true;}
        if(sequence==receipt.Sequence){outcome=fingerprint==receipt.Fingerprint?receipt.Outcome:new JobOutcome(JobResult.StaleCommand);return true;}return false;
    }
    private JobOutcome CommitMail(ulong sequence,ulong fingerprint,JobOutcome outcome)
    {ledger.Receipts[state.ActiveProfile]=new JobCommandReceipt{Sequence=sequence,Fingerprint=fingerprint,Outcome=outcome};ledger.Revision++;return outcome;}
    public JobOutcome SendLetter(JobIdentity recipient,string body,int paperPocket,int giftPocket,ulong sequence)
    {
        if(!Enabled||state.Visiting)return new JobOutcome(JobResult.NotAvailable);
        ulong fingerprint=MailFingerprint(JobCommandKind.SendMail,recipient,paperPocket,giftPocket,body);
        if(MailReceipt(sequence,fingerprint,out var prior))return prior;
        if(!JobLetterGrader.TryGrade(body,giftPocket>=0,out var grade))return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.InvalidLetter));
        int npc=ledger.FindNpc(recipient),player=ledger.FindPlayer(recipient);
        if(npc<0&&player<0)return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.InvalidIdentity));
        if((uint)paperPocket>=state.Capacity||state.IsJobItem(paperPocket)||!Catalog.Has(state.Pockets[paperPocket],ItemFlags.Stationery))return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.WrongItem));
        int gift=0;if(giftPocket>=0){if((uint)giftPocket>=state.Capacity||giftPocket==paperPocket||state.IsJobItem(giftPocket)||!Catalog.Has(state.Pockets[giftPocket],ItemFlags.Gift)||state.Pockets[giftPocket]==JobContent.Letter)return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.WrongItem));gift=state.Pockets[giftPocket];}
        ledger.EnsureMail();int slot=-1;for(int i=OfflineJobLedger.InboxCount;i<ledger.Mail.Length;i++)if(ledger.Mail[i].State==JobMailState.Empty){slot=i;break;}
        if(slot<0)return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.FullOutbox));
        int reward=npc>=0?ledger.FreeReward():-1;if(npc>=0&&reward<0)return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.FullRewards));
        if(!ledger.CanAllocate(npc>=0?2:1))return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.InvalidIdentity));
        var packed=JobTextBody.Pack(grade.NormalizedBody,grade.ScalarCount);
        var sender=ledger.Player(state.ActiveProfile);var id=ledger.Allocate();var reservation=npc>=0?ledger.Allocate():default;
        // For original letters ReplyText 16..27 caches the immutable v1 grade.
        // Replies use ordinary text IDs; older queued letters keep zero and
        // take the legacy grading path once, at delivery.
        long due=NextPost(Now);var envelope=new JobMailRecord{Id=id,Owner=sender,Sender=sender,Recipient=recipient,Reservation=reservation,State=JobMailState.Queued,Gift=gift,ReplyText=16+grade.Rank,SentAt=Now,DueAt=due,Body=null};ledger.StoreBody(ref envelope,packed);ledger.Mail.Set(slot,envelope);
        if(reward>=0)ledger.Rewards[reward]=new VillagerJobReward{Id=reservation,Job=id,Owner=sender,Giver=recipient,State=JobRewardState.Reserved};
        state.Pockets[paperPocket]=0;if(giftPocket>=0)state.Pockets[giftPocket]=0;
        return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.Ok,id));
    }
    private static long NextPost(long now)
    {long day=now/86400;if(now%86400<0)day--;long at=day*86400+8*3600;return at>now?at:at+86400;}
    partial void ProcessScheduledMail(long now)
    {
        ulong attempted=0;
        for(int pass=0;pass<OfflineJobLedger.OutboxCapacity;pass++){
            int next=-1;for(int i=OfflineJobLedger.InboxCount;i<ledger.Mail.Length;i++){
                var m=ledger.Mail[i];if(m.State!=JobMailState.Queued||m.DueAt>now||(attempted&(1UL<<(i-OfflineJobLedger.InboxCount)))!=0)continue;
                if(next<0||m.DueAt<ledger.Mail[next].DueAt||m.DueAt==ledger.Mail[next].DueAt&&JobIdentity.Compare(m.Id,ledger.Mail[next].Id)<0)next=i;
            }
            if(next<0)break;attempted|=1UL<<(next-OfflineJobLedger.InboxCount);
            var message=ledger.Mail[next];long eventAt=message.DueAt;if(ledger.Watermark!=long.MinValue&&eventAt<ledger.Watermark)eventAt=ledger.Watermark;
            Now=eventAt;Reconcile(eventAt);
            int player=ledger.FindPlayer(message.Recipient),npc=ledger.FindNpc(message.Recipient);
            bool delivered=false;
            if(player>=0)delivered=InsertIncoming(player,message);
            else if(npc>=0){DeliverNpcLetter(message,npc);delivered=true;}
            else{
                int reservation=ledger.FindReward(message.Reservation);
                if(reservation>=0){var r=ledger.Rewards[reservation];r.State=JobRewardState.Owed;r.IsReply=true;r.AutomaticReply=true;r.Item=message.Gift;r.ReplyText=255;r.ReplyAt=eventAt;r.ResolvedAt=eventAt;ledger.Rewards[reservation]=r;}
                delivered=true;
            }
            if(delivered){ledger.Mail.Set(next,default);ledger.EventSequences[0]++;ledger.Revision++;}ledger.Watermark=eventAt;
        }
        Now=now;
        for(int i=0;i<ledger.Rewards.Length;i++){var r=ledger.Rewards[i];if(r.State==JobRewardState.Owed&&r.IsReply&&r.AutomaticReply&&r.ReplyAt<=now){int owner=ledger.FindPlayer(r.Owner);if(owner>=0)InsertReply(i,owner);}}
    }
    private void DeliverNpcLetter(JobMailRecord message,int npc)
    {
        int rank=message.ReplyText-16;
        if(rank<0||rank>11||(rank>=6)!=(message.Gift!=0)){
            string body=JobTextBody.Unpack(message.Body!,message.BodyOffset,message.BodyWords);
            if(!JobLetterGrader.TryGrade(body,message.Gift!=0,out var grade))throw new InvalidOperationException("Invalid queued letter body");rank=grade.Rank;
        }
        int sender=ledger.FindPlayer(message.Sender);if(sender<0)return;
        Friendship(message.Sender,message.Recipient,3-(rank%6>=3?0:5)+(message.Gift!=0?3:0));
        int favor=OfflineJobLedger.SharedStart+npc;var j=ledger.Jobs[favor];
        bool qualifies=sender<GameState.ProfileCount&&j.Occupying&&j.Template==JobTemplate.FavorLetter&&j.Status==JobStatus.Active&&Now>=j.AcceptedAt&&Now<=j.DueAt;
        int reservation=ledger.FindReward(qualifies?j.Obligation:message.Reservation);
        if(reservation<0)throw new InvalidOperationException("Delivered letter lost its reserved reply");
        var reward=ledger.Rewards[reservation];reward.Owner=message.Sender;reward.Giver=message.Recipient;reward.State=JobRewardState.Owed;reward.IsReply=true;reward.ResolvedAt=Now;
        reward.ReplyRank=rank;reward.ReplyText=rank;reward.AutomaticReply=!qualifies;reward.ReplyAt=NextPost(Now);reward.Item=ReplyPresent(rank);
        ledger.Rewards[reservation]=reward;
        if(qualifies){
            int extra=ledger.FindReward(message.Reservation);if(extra>=0&&extra!=reservation)ledger.Rewards[extra]=default;
            j.Claimant=message.Sender;RecordEnd(j,JobEndReason.Success);j.Evidence=message.Id;j.EvidenceAt=Now;j.CompletedAt=Now;j.Status=JobStatus.ReplyOwed;j.Revision++;ledger.Jobs[favor]=j;
        }
    }
    private int ReplyPresent(int rank)
    {
        if(rank<3)return 0;
        if(rank==6)return ledger.NativeFruit;
        if(rank==9)return JobContent.PickTagged(RewardRandom,ItemFlags.Fruit,ledger.NativeFruit);
        JobRewardCategory category=rank==3||rank==7||rank==8?JobRewardCategory.Clothing:rank==4||rank==10?JobRewardCategory.Furniture:RewardRandom.NextInt(2)==0?JobRewardCategory.Flooring:JobRewardCategory.Wallpaper;
        // This catalog has no rarity taxonomy. Rules explicitly alias each rare
        // pool to its ordinary pool; replies never use the house-cache branch.
        return JobContent.PickCategory(RewardRandom,category);
    }
    private bool InsertIncoming(int profile,JobMailRecord message)
    {
        int count=profile==state.ActiveProfile?state.MailCount:state.ProfileAt(profile).MailCount;
        if(count>=OfflineJobLedger.MailboxCapacity)return false;
        message.State=JobMailState.Inbox;message.Owner=ledger.Player(profile);message.DueAt=Now;
        ledger.Mail.Set(profile*OfflineJobLedger.MailboxCapacity+count,message);
        if(profile==state.ActiveProfile){state.Mail[state.MailCount++]=JobContent.Letter;}else state.ProfileAt(profile).Mail[state.ProfileAt(profile).MailCount++]=JobContent.Letter;
        return true;
    }
    private JobOutcome InsertReply(int rewardSlot,int profile)
    {
        var reward=ledger.Rewards[rewardSlot];int count=profile==state.ActiveProfile?state.MailCount:state.ProfileAt(profile).MailCount;
        // The save format has four resident inboxes. A guest reply is settled
        // directly into town custody rather than written over the outbox slots.
        if(profile==OfflineJobLedger.Visitor){
            if(!state.Visiting||reward.Job.Town!=ledger.TownId)return new JobOutcome(JobResult.NotAvailable,reward.Job);
            int gift=reward.Item;
            if(gift!=0&&!state.Town.LostAndFound.HoldVisitorReward(state,rewardSlot))return new JobOutcome(JobResult.NotAvailable,reward.Job);
            reward=ledger.Rewards[rewardSlot];
            if(reward.Item==0&&reward.Currency==0)ledger.Rewards[rewardSlot]=default;
            int guestJob=ledger.FindJob(reward.Job);if(guestJob>=0&&ledger.Jobs[guestJob].Template==JobTemplate.FavorLetter)ledger.Jobs[guestJob]=default;
            return new JobOutcome(JobResult.Ok,reward.Job,item:gift);
        }
        if(count>=OfflineJobLedger.MailboxCapacity)return new JobOutcome(JobResult.FullMailbox,reward.Job);
        if(!ledger.CanAllocate(1))return new JobOutcome(JobResult.InvalidIdentity,reward.Job);
        ledger.EnsureMail();var letter=new JobMailRecord{Id=ledger.Allocate(),Owner=reward.Owner,Sender=reward.Giver,Recipient=reward.Owner,State=JobMailState.Inbox,Gift=reward.Item,ReplyText=reward.ReplyText,Reply=true,SentAt=reward.ResolvedAt,DueAt=Now};
        if(!InsertIncoming(profile,letter))return new JobOutcome(JobResult.FullMailbox,reward.Job);
        ledger.Rewards[rewardSlot]=default;int job=ledger.FindJob(reward.Job);if(job>=0&&ledger.Jobs[job].Template==JobTemplate.FavorLetter)ledger.Jobs[job]=default;
        return new JobOutcome(JobResult.Ok,reward.Job,item:reward.Item);
    }
    private JobOutcome CollectReply(int rewardSlot)
    {var reward=ledger.Rewards[rewardSlot];if(reward.AutomaticReply&&Now<reward.ReplyAt)return new JobOutcome(JobResult.NotReady,reward.Job);return InsertReply(rewardSlot,state.ActiveProfile);}
    partial void ShiftScheduledMail(long delta)
    {
        for(int i=0;i<ledger.Mail.Length;i++){var message=ledger.Mail[i];if(message.State==JobMailState.Queued){message.SentAt+=delta;message.DueAt+=delta;ledger.Mail.Set(i,message);}}
        for(int i=0;i<ledger.Rewards.Length;i++)if(ledger.Rewards[i].State==JobRewardState.Owed&&ledger.Rewards[i].IsReply)ledger.Rewards[i].ReplyAt+=delta;
    }
    public string ReadLetter(int slot)
    {
        if((uint)slot>=state.MailCount)return "";var message=ledger.Letter(state.ActiveProfile,slot);
        if(message.State!=JobMailState.Inbox)return "Thank you for being a part of our town!";
        if(!message.Read){message.Read=true;ledger.Mail.Set(state.ActiveProfile*OfflineJobLedger.MailboxCapacity+slot,message);ledger.Revision++;}
        if(message.Reply)return ReplyBody(message.ReplyText);
        string body=JobTextBody.Unpack(message.Body!,message.BodyOffset,message.BodyWords);return body;
    }
    private static string ReplyBody(int key)
    {
        if(key==254)return "Town hall recovered this present from a neighbour who has left. Your earned reward is enclosed. Thank you for helping our town.";
        if(key==255)return "Your letter could not reach its recipient. Your present is enclosed.";
        string[] replies={"Thank you for thinking of me. A little letter is always welcome.","Your note reached me! I hope we can chat again soon.","Thanks for your letter. It is good to hear from a neighbour.","Your thoughtful words brightened my day. I picked something for you.","What a lovely letter! Here is a little something for your home.","I enjoyed your letter. Perhaps this will make your room feel special.","Thank you for the present. I thought you might enjoy some fruit.","Your letter made me smile. I found something you might like to wear.","Thank you for writing and for your gift. This shirt is for you.","Your kind letter deserves a treat. Try this fruit from another town.","Your letter and present were wonderful! I chose something for your home.","I will treasure your letter. I hope this makes your home a little happier."};
        return replies[key<0||key>=replies.Length?0:key];
    }
    public JobOutcome TakeMailPresent(int slot,ulong sequence)
    {
        if(!Enabled||state.Visiting||(uint)slot>=state.MailCount)return new JobOutcome(JobResult.NotAvailable);
        var letter=ledger.Letter(state.ActiveProfile,slot);ulong fingerprint=MailFingerprint(JobCommandKind.TakePresent,letter.Id,0,0,"");
        if(MailReceipt(sequence,fingerprint,out var prior))return prior;
        if(letter.State!=JobMailState.Inbox||letter.Owner!=ledger.Player(state.ActiveProfile)||letter.Gift==0)return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.WrongItem));
        if(!state.Receive(letter.Gift))return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.FullPockets));
        int gift=letter.Gift;letter.Gift=0;ledger.Mail.Set(state.ActiveProfile*OfflineJobLedger.MailboxCapacity+slot,letter);
        return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.Ok,letter.Id,item:gift));
    }
    public JobOutcome DiscardLetter(int slot,bool discardPresent,ulong sequence)
    {
        if(!Enabled||(uint)slot>=state.MailCount)return new JobOutcome(JobResult.NotAvailable);
        var letter=ledger.Letter(state.ActiveProfile,slot);ulong fingerprint=MailFingerprint(JobCommandKind.DiscardMail,letter.Id,discardPresent?1:0,0,"");
        if(MailReceipt(sequence,fingerprint,out var prior))return prior;
        if(letter.Gift!=0&&!discardPresent)return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.WrongItem));
        RemoveMailboxSlot(slot);return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.Ok,letter.Id));
    }
    public void RemoveMailboxSlot(int slot)
    {
        if((uint)slot>=state.MailCount)return;int last=--state.MailCount,first=state.ActiveProfile*OfflineJobLedger.MailboxCapacity;
        state.Mail[slot]=state.Mail[last];state.Mail[last]=0;
        if(ledger.Mail.Length!=0){ledger.Mail.Set(first+slot,ledger.Mail[first+last]);ledger.Mail.Set(first+last,default);}
    }
}
