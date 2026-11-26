namespace AnimalTown.Jobs;

public struct VillagerJobRecord
{
    public JobIdentity Id,Owner,AcceptedBy,Claimant,Requester,Target,Obligation,Binding,Evidence,NextBorrower;
    public JobIdentity Borrower1,Borrower2,Borrower3,Borrower4;
    public JobTemplate Template;
    public JobStatus Status;
    public int Revision,Item,FlowerBaseline,HomeAcre,Borrowers,ChainDecision;
    public long AcceptedAt,StageAt,OriginalDueAt,DueAt,CompletedAt,RetainedUntil,EvidenceAt;
    public bool Shared=>Template>=JobTemplate.FavorFruit;
    public bool Occupying=>Status!=JobStatus.Empty;
    public JobIdentity Borrower(int index)=>index==0?Borrower1:index==1?Borrower2:index==2?Borrower3:Borrower4;
    public void SetBorrower(int index,JobIdentity id){if(index==0)Borrower1=id;else if(index==1)Borrower2=id;else if(index==2)Borrower3=id;else Borrower4=id;}
}
public struct VillagerJobOffer
{
    public JobIdentity Id,Owner,Requester,Target;
    public JobTemplate Template;
    public int Item,Revision;
    public long CreatedAt;
    public bool Refused;
}
public enum JobRewardState { Empty,Reserved,Owed,Custody }
public struct VillagerJobReward
{
    public JobIdentity Id,Job,Owner,Giver;
    public JobRewardState State;
    public int Item,Currency,ReplyRank,ReplyText;
    public JobRewardCategory Category;
    public int MigratedFromItem;
    public long ResolvedAt;
    public long ReplyAt;
    public bool IsReply,AutomaticReply,PortableOnline;
}
public struct JobRelation
{
    public JobIdentity Npc;
    public int Friendship,Met;
}
public struct JobCommandReceipt
{
    public ulong Sequence,Fingerprint;
    public JobOutcome Outcome;
}

public sealed partial class OfflineJobLedger
{
    public const int PersonalPerPlayer=20,PersonalCount=OwnerCount*PersonalPerPlayer;
    public const int SharedStart=PersonalCount,JobCapacity=PersonalCount+TownJobState.ResidentCount;
    public const int OfferCapacity=48,RewardCapacity=128,RelationsPerPlayer=64;
    private static readonly VillagerJobRecord[] emptyJobs=new VillagerJobRecord[0];
    private static readonly VillagerJobOffer[] emptyOffers=new VillagerJobOffer[0];
    private static readonly VillagerJobReward[] emptyRewards=new VillagerJobReward[0];
    private static readonly JobIdentity[] emptyKeys=new JobIdentity[0];
    private static readonly JobRelation[] emptyRelations=new JobRelation[0];
    private static readonly JobCommandReceipt[] emptyReceipts=new JobCommandReceipt[0];
    internal VillagerJobRecord[] Jobs=emptyJobs;
    internal VillagerJobOffer[] Offers=emptyOffers;
    internal VillagerJobReward[] Rewards=emptyRewards;
    internal JobIdentity[] Bindings=emptyKeys;
    internal JobRelation[] Relations=emptyRelations;
    internal JobCommandReceipt[] Receipts=emptyReceipts;
    internal readonly ulong[] EventSequences=new ulong[3];
    internal readonly JobIdentity[] ForeignNpcs=new JobIdentity[OwnerCount];
    internal readonly ulong[] ForeignTowns=new ulong[OwnerCount];
    internal readonly int[] FurnitureCacheItems=new int[6],FurnitureCacheVersions=new int[6];
    public int NativeFruit {get;internal set;}=JobContent.NativeFruit;
    public JobIdentity LastMovedNpc {get;internal set;}
    public ulong LastMovedTown {get;internal set;}
    public long Watermark {get;internal set;}=long.MinValue;
    internal void EnsureRecords()
    {
        if(Jobs.Length!=0)return;
        Jobs=new VillagerJobRecord[JobCapacity];Offers=new VillagerJobOffer[OfferCapacity];Rewards=new VillagerJobReward[RewardCapacity];
        Bindings=new JobIdentity[OwnerCount*GameState.MaximumPockets];Relations=new JobRelation[OwnerCount*RelationsPerPlayer];Receipts=new JobCommandReceipt[OwnerCount];
    }
    public VillagerJobRecord Job(int slot)=>Jobs[slot];
    public VillagerJobOffer Offer(int profile,int npc){int at=OfferSlot(profile,npc,false);return at<0?default:Offers[at];}
    internal int OfferSlot(int profile,int npc,bool create)
    {int free=-1;var owner=Player(profile);var requester=Npc(npc);for(int i=0;i<Offers.Length;i++){var o=Offers[i];if(o.Owner==owner&&o.Requester==requester&&!o.Id.IsEmpty)return i;if(o.Id.IsEmpty&&free<0)free=i;}return create?free:-1;}
    public VillagerJobReward Reward(int slot)=>Rewards[slot];
    public int FindJob(JobIdentity id){if(id.IsEmpty)return -1;for(int i=0;i<Jobs.Length;i++)if(Jobs[i].Occupying&&Jobs[i].Id==id)return i;return -1;}
    public int FindReward(JobIdentity id){if(id.IsEmpty)return -1;for(int i=0;i<Rewards.Length;i++)if(Rewards[i].State!=JobRewardState.Empty&&Rewards[i].Id==id)return i;return -1;}
    internal int FreeReward(){for(int i=0;i<Rewards.Length;i++)if(Rewards[i].State==JobRewardState.Empty)return i;return -1;}
    internal int FreePersonal(int profile,JobFamily family)
    {int first=profile*PersonalPerPlayer+(family==JobFamily.Retrieval?15:0),count=family==JobFamily.Retrieval?5:15;for(int i=first;i<first+count;i++)if(!Jobs[i].Occupying)return i;return -1;}
    public bool HasBinding(int profile,int slot)=>Initialized&&(uint)slot<GameState.MaximumPockets&&!Bindings[profile*GameState.MaximumPockets+slot].IsEmpty;
    public JobIdentity Binding(int profile,int slot)=>HasBinding(profile,slot)?Bindings[profile*GameState.MaximumPockets+slot]:default;
    internal void SetBinding(int profile,int slot,JobIdentity binding){Bindings[profile*GameState.MaximumPockets+slot]=binding;}
    internal int RelationSlot(int profile,JobIdentity npc,bool create)
    {
        int first=profile*RelationsPerPlayer,free=-1;
        for(int i=first;i<first+RelationsPerPlayer;i++){if(Relations[i].Npc==npc&&!npc.IsEmpty)return i;if(Relations[i].Npc.IsEmpty&&free<0)free=i;}
        if(!create||free<0)return -1;Relations[free].Npc=npc;return free;
    }
    internal void RememberRelation(int profile,JobIdentity npc,int friendship,int met)
    {int at=RelationSlot(profile,npc,true);if(at<0)throw new InvalidOperationException("Villager relationship history is full");Relations[at].Friendship=friendship;Relations[at].Met=met;}
    private void WriteRecords(JobWireWriter w,bool runtimeProjection=false)
    {
        w.Int(NativeFruit);for(int i=0;i<6;i++){w.Int(FurnitureCacheItems[i]);w.Int(FurnitureCacheVersions[i]);}
        w.Long(runtimeProjection?long.MinValue:Watermark);for(int i=0;i<EventSequences.Length;i++)w.ULong(EventSequences[i]);
        w.Key(LastMovedNpc);w.ULong(LastMovedTown);
        for(int p=0;p<OwnerCount;p++){w.Key(ForeignNpcs[p]);w.ULong(ForeignTowns[p]);w.ULong(Receipts[p].Sequence);w.ULong(Receipts[p].Fingerprint);w.Int((int)Receipts[p].Outcome.Result);w.Key(Receipts[p].Outcome.Job);w.Int(Receipts[p].Outcome.Amount);w.Int(Receipts[p].Outcome.Item);}
        int count=0;for(int i=0;i<Jobs.Length;i++)if(Jobs[i].Occupying)count++;w.Int(count);
        for(int i=0;i<Jobs.Length;i++)if(Jobs[i].Occupying){var j=Jobs[i];w.Int(i);w.Key(j.Id);w.Key(j.Owner);w.Key(j.AcceptedBy);w.Key(j.Claimant);w.Key(j.Requester);w.Key(j.Target);w.Key(j.Obligation);w.Key(j.Binding);w.Key(j.Evidence);w.Key(j.NextBorrower);
            w.Int((int)j.Template);w.Int((int)j.Status);w.Int(j.Revision);w.Int(j.Item);w.Int(j.FlowerBaseline);w.Int(j.HomeAcre);w.Int(j.Borrowers);w.Int(j.ChainDecision);
            w.Long(j.AcceptedAt);w.Long(j.StageAt);w.Long(j.OriginalDueAt);w.Long(j.DueAt);w.Long(j.CompletedAt);w.Long(j.RetainedUntil);w.Long(j.EvidenceAt);for(int b=0;b<JobRulesV1.MaximumRecordedBorrowers;b++)w.Key(j.Borrower(b));}
        count=0;for(int i=0;i<Offers.Length;i++)if(!Offers[i].Id.IsEmpty)count++;w.Int(count);
        for(int i=0;i<Offers.Length;i++)if(!Offers[i].Id.IsEmpty){var o=Offers[i];w.Int(i);w.Key(o.Id);w.Key(o.Owner);w.Key(o.Requester);w.Key(o.Target);w.Int((int)o.Template);w.Int(o.Item);w.Int(o.Revision);w.Long(o.CreatedAt);w.Int(o.Refused?1:0);}
        count=0;for(int i=0;i<Rewards.Length;i++)if(Rewards[i].State!=JobRewardState.Empty)count++;w.Int(count);
        for(int i=0;i<Rewards.Length;i++)if(Rewards[i].State!=JobRewardState.Empty){var r=Rewards[i];w.Int(i);w.Key(r.Id);w.Key(r.Job);w.Key(r.Owner);w.Key(r.Giver);w.Int((int)r.State);w.Int(r.Item);w.Int(r.Currency);w.Int(r.ReplyRank);w.Int(r.ReplyText);w.Long(r.ResolvedAt);w.Int(r.IsReply?1:0);w.Long(r.ReplyAt);w.Int(r.AutomaticReply?1:0);w.Int((int)r.Category);w.Int(r.MigratedFromItem);w.Int(r.PortableOnline?1:0);}
        count=0;for(int i=0;i<Bindings.Length;i++)if(!Bindings[i].IsEmpty)count++;w.Int(count);for(int i=0;i<Bindings.Length;i++)if(!Bindings[i].IsEmpty){w.Int(i);w.Key(Bindings[i]);}
        count=0;for(int i=0;i<Relations.Length;i++)if(!Relations[i].Npc.IsEmpty)count++;w.Int(count);for(int i=0;i<Relations.Length;i++)if(!Relations[i].Npc.IsEmpty){w.Int(i);w.Key(Relations[i].Npc);w.Int(Relations[i].Friendship);w.Int(Relations[i].Met);}
    }
    private void ReadRecords(JobWireReader r,int version)
    {
        NativeFruit=r.Range(1,Catalog.Items.Length-1);if(!Catalog.Has(NativeFruit,ItemFlags.Fruit))throw new InvalidOperationException("Invalid native fruit");for(int i=0;i<6;i++){FurnitureCacheItems[i]=r.Range(0,Catalog.Items.Length-1);FurnitureCacheVersions[i]=r.Int();}
        Watermark=r.Long();for(int i=0;i<EventSequences.Length;i++)EventSequences[i]=r.ULong();LastMovedNpc=r.Key();LastMovedTown=r.ULong();
        for(int p=0;p<OwnerCount;p++){ForeignNpcs[p]=r.Key();ForeignTowns[p]=r.ULong();Receipts[p].Sequence=r.ULong();Receipts[p].Fingerprint=r.ULong();var result=(JobResult)r.Range(0,(int)JobResult.NoOffer);var id=r.Key();int amount=r.Range(0,int.MaxValue),item=r.Range(0,Catalog.Items.Length-1);Receipts[p].Outcome=new JobOutcome(result,id,amount,item);}
        int count=r.Range(0,Jobs.Length);for(int n=0;n<count;n++){int i=r.Range(0,Jobs.Length-1);if(Jobs[i].Occupying)throw new InvalidOperationException("Duplicate saved job slot");var j=new VillagerJobRecord();
            j.Id=r.Key(false);j.Owner=r.Key();j.AcceptedBy=r.Key(false);j.Claimant=r.Key();j.Requester=r.Key(false);j.Target=r.Key();j.Obligation=r.Key(false);j.Binding=r.Key();j.Evidence=r.Key();j.NextBorrower=r.Key();
            j.Template=(JobTemplate)r.Range(0,JobRulesV1.TemplateCount-1);j.Status=(JobStatus)r.Range(1,(int)JobStatus.ReplyOwed);j.Revision=r.Range(1,int.MaxValue);j.Item=r.Range(0,Catalog.Items.Length-1);j.FlowerBaseline=r.Range(0,Town.Cells);j.HomeAcre=r.Range(0,Town.ScreenColumns*Town.ScreenRows-1);j.Borrowers=r.Range(0,JobRulesV1.MaximumRecordedBorrowers);j.ChainDecision=r.Range(0,2);
            j.AcceptedAt=r.Long();j.StageAt=r.Long();j.OriginalDueAt=r.Long();j.DueAt=r.Long();j.CompletedAt=r.Long();j.RetainedUntil=r.Long();j.EvidenceAt=r.Long();for(int b=0;b<JobRulesV1.MaximumRecordedBorrowers;b++)j.SetBorrower(b,r.Key());
            if(j.Shared!=(i>=SharedStart)||!j.Shared&&!j.Owner.IsValid||j.StageAt<j.AcceptedAt||j.DueAt<j.StageAt||j.OriginalDueAt<j.AcceptedAt)throw new InvalidOperationException("Invalid saved job ownership or timeline");Jobs[i]=j;}
        count=r.Range(0,Offers.Length);for(int n=0;n<count;n++){int i=r.Range(0,Offers.Length-1);if(!Offers[i].Id.IsEmpty)throw new InvalidOperationException("Duplicate saved offer slot");Offers[i]=new VillagerJobOffer{Id=r.Key(false),Owner=r.Key(false),Requester=r.Key(false),Target=r.Key(),Template=(JobTemplate)r.Range(0,JobRulesV1.TemplateCount-1),Item=r.Range(0,Catalog.Items.Length-1),Revision=r.Range(1,int.MaxValue),CreatedAt=r.Long(),Refused=r.Range(0,1)==1};}
        count=r.Range(0,Rewards.Length);for(int n=0;n<count;n++){
            int i=r.Range(0,Rewards.Length-1);if(Rewards[i].State!=JobRewardState.Empty)throw new InvalidOperationException("Duplicate saved reward slot");
            var reward=new VillagerJobReward{Id=r.Key(false),Job=r.Key(false),Owner=r.Key(),Giver=r.Key(false),State=(JobRewardState)r.Range(1,version>=13?3:2),Item=r.Range(0,version>=11?int.MaxValue:Catalog.Items.Length-1),Currency=r.Range(0,int.MaxValue),ReplyRank=r.Range(0,11),ReplyText=r.Range(0,255),ResolvedAt=r.Long(),IsReply=r.Range(0,1)==1,ReplyAt=r.Long(),AutomaticReply=r.Range(0,1)==1};
            if(version>=11){reward.Category=(JobRewardCategory)r.Range(0,6);reward.MigratedFromItem=r.Range(0,int.MaxValue);}else reward.Category=JobContent.CategoryOf(reward.Item);
            if(version>=14)reward.PortableOnline=r.Range(0,1)==1;
            if(reward.PortableOnline&&(reward.State!=JobRewardState.Owed||reward.IsReply||reward.AutomaticReply||!reward.Owner.IsValid))throw new InvalidOperationException("Invalid portable online reward");
            if(reward.State==JobRewardState.Custody){
                if(reward.Id.Town!=TownId||reward.Job.Town!=TownId||!reward.Owner.IsValid||reward.Item==0&&reward.Currency==0||reward.Item!=0&&!Catalog.Valid(reward.Item)||reward.IsReply||reward.AutomaticReply)throw new InvalidOperationException("Invalid deferred police custody");
            }else JobContent.MigrateReward(ref reward);
            Rewards[i]=reward;
        }
        count=r.Range(0,Bindings.Length);for(int n=0;n<count;n++){int i=r.Range(0,Bindings.Length-1);if(!Bindings[i].IsEmpty)throw new InvalidOperationException("Duplicate saved parcel slot");Bindings[i]=r.Key(false);}
        count=r.Range(0,Relations.Length);for(int n=0;n<count;n++){int i=r.Range(0,Relations.Length-1);if(!Relations[i].Npc.IsEmpty)throw new InvalidOperationException("Duplicate saved relationship slot");Relations[i]=new JobRelation{Npc=r.Key(false),Friendship=r.Range(0,255),Met=r.Range(0,1)};}
    }
}
