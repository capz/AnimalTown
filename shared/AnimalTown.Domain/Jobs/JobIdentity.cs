namespace AnimalTown.Jobs;

// Namespace is a persisted town nonce, not its terrain seed. Serial numbers are
// shared by all entity kinds and never recycled, including after profile reuse.
public readonly struct JobIdentity : IEquatable<JobIdentity>
{
    public readonly ulong Town,Serial;
    public bool IsEmpty=>Town==0&&Serial==0;
    public bool IsValid=>Town!=0&&Serial!=0;
    public JobIdentity(ulong town,ulong serial){Town=town;Serial=serial;}
    public bool Equals(JobIdentity other)=>Town==other.Town&&Serial==other.Serial;
    public override bool Equals(object? other)=>other is JobIdentity key&&Equals(key);
    public override int GetHashCode()=>unchecked((int)(Town^(Town>>32)^Serial^(Serial>>32)));
    public static bool operator ==(JobIdentity a,JobIdentity b)=>a.Equals(b);
    public static bool operator !=(JobIdentity a,JobIdentity b)=>!a.Equals(b);
    public static int Compare(JobIdentity a,JobIdentity b)
    {if(a.Town!=b.Town)return a.Town<b.Town?-1:1;if(a.Serial==b.Serial)return 0;return a.Serial<b.Serial?-1:1;}
}

public enum JobStatus { Empty,Active,Seeking,Returning,ReadyToReport,RewardOwed,CompletedRetained,ReplyOwed }
public enum JobEndReason { None,Success,Declined,Abandoned,Expired,SeasonClosed,WorldRemoved,BrokenBinding,ProfileDeleted }
public enum JobResult
{
    Ok,NoWork,ExistingOffer,NotAvailable,InvalidContext,StaleCommand,AlreadyProcessed,
    FullPockets,FullJobs,FullRewards,FullMailbox,WrongItem,WrongNpc,AlreadyClaimed,
    NotReady,Expired,TravelUnavailable,InvalidContent,InvalidIdentity,ExistingJob,Retained,OfferInvalidated,FullOutbox,InvalidLetter,FullOffers,NoOffer
}
public enum JobCommandKind { Ask,Accept,Decline,Handoff,Report,Collect,Abandon,SupplyCancel,LetterFollowup,SendMail,TakePresent,DiscardMail,PostNotice }
public enum JobEventKind { MailDelivered,WorldObjectChanged,NpcMigrated,BallCollision,SnowmanCreated,SnowmanDestroyed }

public readonly struct JobOutcome
{
    public readonly JobResult Result;
    public readonly JobIdentity Job;
    public readonly int Amount,Item;
    public JobOutcome(JobResult result,JobIdentity job=default,int amount=0,int item=0)
    {Result=result;Job=job;Amount=amount;Item=item;}
}
