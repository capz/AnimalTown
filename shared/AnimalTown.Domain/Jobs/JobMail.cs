namespace AnimalTown.Jobs;

public enum JobMailState { Empty,Queued,Inbox }
public struct JobMailRecord
{
    public JobIdentity Id,Owner,Sender,Recipient,Reservation;
    public JobMailState State;
    public int Gift,ReplyText;
    public bool Read,Reply;
    public long SentAt,DueAt;
    internal int[]? Body;
}
public sealed partial class OfflineJobLedger
{
    public const int MailboxCapacity=128,InboxCount=GameState.ProfileCount*MailboxCapacity,OutboxCapacity=64;
    private static readonly JobMailRecord[] emptyMail=new JobMailRecord[0];
    internal JobMailRecord[] Mail=emptyMail;
    internal void EnsureMail(){if(Mail.Length==0)Mail=new JobMailRecord[InboxCount+OutboxCapacity];}
    public JobMailRecord Letter(int profile,int slot)
    {if((uint)profile>=GameState.ProfileCount||(uint)slot>=MailboxCapacity||Mail.Length==0)return default;return Mail[profile*MailboxCapacity+slot];}
    private void WriteMail(JobWireWriter w)
    {
        int count=0;for(int i=0;i<Mail.Length;i++)if(Mail[i].State!=JobMailState.Empty)count++;w.Int(count);
        for(int i=0;i<Mail.Length;i++)if(Mail[i].State!=JobMailState.Empty){var m=Mail[i];w.Int(i);w.Key(m.Id);w.Key(m.Owner);w.Key(m.Sender);w.Key(m.Recipient);w.Key(m.Reservation);w.Int((int)m.State);w.Int(m.Gift);w.Int(m.ReplyText);w.Int(m.Read?1:0);w.Int(m.Reply?1:0);w.Long(m.SentAt);w.Long(m.DueAt);w.Block(m.Body);}
    }
    private void ReadMail(JobWireReader r)
    {
        int count=r.Range(0,InboxCount+OutboxCapacity);if(count>0)EnsureMail();
        for(int n=0;n<count;n++){
            int at=r.Range(0,Mail.Length-1);if(Mail[at].State!=JobMailState.Empty)throw new InvalidOperationException("Duplicate saved letter slot");
            var m=new JobMailRecord{Id=r.Key(false),Owner=r.Key(false),Sender=r.Key(false),Recipient=r.Key(false),Reservation=r.Key(),State=(JobMailState)r.Range(1,2),Gift=r.Range(0,Catalog.Items.Length-1),ReplyText=r.Range(0,255),Read=r.Range(0,1)==1,Reply=r.Range(0,1)==1,SentAt=r.Long(),DueAt=r.Long(),Body=r.Block(1001)};
            if((m.State==JobMailState.Inbox)!=(at<InboxCount)||m.DueAt<m.SentAt||!m.Reply&&m.Body==null||m.Body!=null&&(m.Body.Length<1||m.Body[0]<0||m.Body[0]>4000||m.Body.Length!=(m.Body[0]+3)/4+1))throw new InvalidOperationException("Invalid saved letter envelope");
            Mail[at]=m;
        }
    }
}
