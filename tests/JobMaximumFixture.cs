using AnimalTown;
using AnimalTown.Jobs;

// Synthetic maximum occupancy, in addition to the real-command fixtures.
// Four permanent owners fill 15/5 personal partitions and all six shared slots.
internal static class JobMaximumFixture
{
    internal static void Fill(GameState state,string body)
    {
        var l=state.Town.Jobs;long now=l.Watermark;int rewards=0;
        for(int p=0;p<4;p++)for(int n=0;n<20;n++){
            var id=l.Allocate();var obligation=l.Allocate();bool retrieval=n>=15;
            l.Jobs[p*20+n]=new VillagerJobRecord{Id=id,Owner=l.Player(p),AcceptedBy=l.Player(p),Claimant=l.Player(p),Requester=l.Npc(0),Target=l.Npc(0),Obligation=obligation,Template=retrieval?JobTemplate.RetrievalBorrowed:JobTemplate.DeliveryClothing,Status=JobStatus.RewardOwed,Revision=1,Item=retrieval?Catalog.Notebook:Catalog.LeafShirt,AcceptedAt=now,StageAt=now,OriginalDueAt=now+86400,DueAt=now+86400,CompletedAt=now,Borrowers=retrieval?1:0,Borrower1=retrieval?l.Npc(1):default};
            l.Rewards[rewards++]=new VillagerJobReward{Id=obligation,Job=id,Owner=l.Player(p),Giver=l.Npc(0),State=JobRewardState.Owed,Item=Catalog.LeafShirt,Category=JobRewardCategory.Clothing,ResolvedAt=now};
        }
        for(int n=0;n<6;n++){
            var id=l.Allocate();var obligation=l.Allocate();l.Jobs[OfflineJobLedger.SharedStart+n]=new VillagerJobRecord{Id=id,AcceptedBy=l.Player(0),Claimant=l.Player(0),Requester=l.Npc(n),Obligation=obligation,Template=(JobTemplate)((int)JobTemplate.FavorFruit+n),Status=JobStatus.CompletedRetained,Revision=1,AcceptedAt=now,StageAt=now,OriginalDueAt=now+86400,DueAt=now+86400,CompletedAt=now,RetainedUntil=now+4*86400};
            l.Rewards[rewards++]=new VillagerJobReward{Id=obligation,Job=id,Owner=l.Player(0),Giver=l.Npc(n),State=JobRewardState.Owed,Item=Catalog.OakFloor,Category=JobRewardCategory.Flooring,ResolvedAt=now};
        }
        for(int i=0;i<48;i++)l.Offers[i]=new VillagerJobOffer{Id=l.Allocate(),Owner=new JobIdentity(90000,(ulong)(i/6+1)),Requester=l.Npc(i%6),Target=l.Npc((i+1)%6),Template=JobTemplate.DeliveryProperty,Item=Catalog.Notebook,Revision=1,CreatedAt=now};
        for(int i=0;i<64;i++){
            var id=l.Allocate();bool npc=rewards<OfflineJobLedger.RewardCapacity;var reservation=npc?l.Allocate():default;
            var m=new JobMailRecord{Id=id,Owner=l.Player(0),Sender=l.Player(0),Recipient=npc?l.Npc(0):l.Player(1),Reservation=reservation,State=JobMailState.Queued,SentAt=now,DueAt=now+86400};l.StoreBody(ref m,JobTextBody.Pack(body,1000));l.Mail.Set(OfflineJobLedger.InboxCount+i,m);
            if(npc)l.Rewards[rewards++]=new VillagerJobReward{Id=reservation,Job=id,Owner=l.Player(0),Giver=l.Npc(0),State=JobRewardState.Reserved};
        }
        for(int i=0;i<8;i++)l.Notices[i]=new TownNotice{Id=l.Allocate(),Author=l.Player(0),At=now,Text=body};
    }
}
