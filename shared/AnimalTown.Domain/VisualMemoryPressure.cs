namespace AnimalTown;

// System headroom includes every native owner, rather than adding overlapping
// model/texture budget charges. Free bytes do not promise a contiguous allocation.
public sealed class VisualMemoryPressure
{
    public const int LowBytes=1536*1024,HighBytes=2560*1024,SafetyBytes=256*1024;
    public const long FailureBackoffUs=1000000,RecoveryGraceUs=3000000;
    public bool UnderPressure {get;private set;}
    public bool Available {get;private set;}
    public int FreeBytes {get;private set;}
    public int FailureCount {get;private set;}
    public int FailedRequestBytes {get;private set;}
    public long RetryAt {get;private set;}
    private long recoveredAt=-1;
    private readonly int[] tickets=new int[32],reservations=new int[32];
    private readonly bool[] completed=new bool[32];
    private int nextTicket=1;
    private int waitingPeak;
    private long waitingUntil;
    public int ReservedBytes {get;private set;}
    public int WaitingPeakBytes=>waitingPeak;
    public bool RequiresObservation {get;private set;}=true;
    public void Observe(int freeBytes,int failures,int failedRequestBytes,long now)
    {
        // Completed requests are reflected by this physical allocator sample.
        // Keep their charge until here so a second owner cannot spend a stale
        // snapshot between an async completion and the next observation.
        for(int i=0;i<tickets.Length;i++)if(completed[i])Release(i);
        RequiresObservation=false;
        bool failed=failures!=FailureCount;
        Available=true;FreeBytes=Math.Max(0,freeBytes);FailureCount=failures;
        if(failed||failedRequestBytes>0)FailedRequestBytes=Math.Max(0,failedRequestBytes);
        if(failed){RetryAt=now+FailureBackoffUs;UnderPressure=true;recoveredAt=-1;}
        if(now>=waitingUntil||(long)FreeBytes-SafetyBytes-ReservedBytes>=waitingPeak)waitingPeak=0;
        if(FreeBytes<LowBytes){UnderPressure=true;recoveredAt=-1;}
        else if(UnderPressure&&FreeBytes>=HighBytes&&now>=RetryAt&&waitingPeak==0){
            if(recoveredAt<0)recoveredAt=now;
            if(now-recoveredAt>=RecoveryGraceUs){UnderPressure=false;recoveredAt=-1;}
        }else recoveredAt=-1;
    }
    public bool CanAdmit(int bytes,long now,bool essential=false)
    {
        if(bytes<0)throw new ArgumentOutOfRangeException("bytes");
        if(bytes==0)return true;
        if(!Available)return true;
        if(now<RetryAt)return false;
        return bytes<=Math.Max(0,(long)FreeBytes-SafetyBytes-ReservedBytes)&&(essential||!UnderPressure);
    }
    // Owner budget/handle rejection can precede malloc, so the real allocator's
    // failure counter is not sufficient on its own to schedule recovery.
    public void AllocationFailed(int requestedBytes,long now)
    {
        if(requestedBytes<0)throw new ArgumentOutOfRangeException("requestedBytes");
        FailedRequestBytes=requestedBytes;RetryAt=now+FailureBackoffUs;UnderPressure=true;recoveredAt=-1;RequiresObservation=true;
    }
    public bool TryReserve(int bytes,long now,bool essential,out int ticket)
    {
        ticket=0;if(!CanAdmit(bytes,now,essential)){
            // An essential restore can need reclamation while total free is
            // still above the ordinary low watermark. Do not invent a malloc
            // failure/backoff; let eligible owners retire immediately.
            if(essential&&bytes>0&&now>=RetryAt){
                if(bytes>waitingPeak)RequiresObservation=true;
                UnderPressure=true;recoveredAt=-1;waitingPeak=Math.Max(waitingPeak,bytes);waitingUntil=now+FailureBackoffUs;
            }
            return false;
        }
        if(bytes==0)return true;
        if(bytes>int.MaxValue-ReservedBytes)return false;
        for(int i=0;i<tickets.Length;i++)if(tickets[i]==0){
            int id=nextTicket++;if(nextTicket<1)nextTicket=1;
            tickets[i]=id;reservations[i]=bytes;completed[i]=false;ReservedBytes+=bytes;ticket=id;return true;
        }
        return false;
    }
    public void Complete(int ticket)
    {
        if(ticket==0)return;
        for(int i=0;i<tickets.Length;i++)if(tickets[i]==ticket){completed[i]=true;RequiresObservation=true;return;}
        throw new InvalidOperationException("Unknown memory reservation");
    }
    public void Cancel(int ticket)
    {
        if(ticket==0)return;
        for(int i=0;i<tickets.Length;i++)if(tickets[i]==ticket){Release(i);return;}
        throw new InvalidOperationException("Unknown memory reservation");
    }
    private void Release(int slot)
    {
        ReservedBytes-=reservations[slot];tickets[slot]=reservations[slot]=0;completed[slot]=false;
    }
}
