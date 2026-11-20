namespace AnimalTown.Jobs;

public enum JobMailState { Empty,Queued,Inbox }
public struct JobMailRecord
{
    public JobIdentity Id,Owner,Sender,Recipient,Reservation;
    public JobMailState State;
    public int Gift,ReplyText;
    internal int BodyOffset,BodyWords;
    public bool Read,Reply;
    public long SentAt,DueAt;
    internal int[]? Body;
}
public sealed partial class OfflineJobLedger
{
    public const int MailboxCapacity=128,InboxCount=GameState.ProfileCount*MailboxCapacity,OutboxCapacity=64;
    private static readonly JobMailSlots emptyMail=new JobMailSlots(0);
    internal JobMailSlots Mail=emptyMail;
    // Short messages share 2 KB slabs. Larger bodies own their exact packed
    // array (at most 2,632 bytes after lossless scalar compaction). Even adversarial discards
    // cannot leave one tiny letter pinning a 16 KB or megabyte-sized arena.
    private const int BodySlabWords=512;
    private int[]? mailBodyArena;private int mailBodyUsed;
    internal void EnsureMail(){if(Mail.Length==0)Mail=new JobMailSlots(InboxCount+OutboxCapacity);}
    public JobMailRecord Letter(int profile,int slot)
    {if((uint)profile>=GameState.ProfileCount||(uint)slot>=MailboxCapacity||Mail.Length==0)return default;return Mail[profile*MailboxCapacity+slot];}
    internal void StoreBody(ref JobMailRecord mail,int[] words)
    {
        if(words.Length>1001)throw new InvalidOperationException("Packed letter exceeds its bound");
        if(words.Length>BodySlabWords){JobMemory.Reclaim();mail.Body=words;mail.BodyOffset=0;mail.BodyWords=words.Length;return;}
        if(mailBodyArena==null||words.Length>mailBodyArena.Length-mailBodyUsed){JobMemory.Reclaim();mailBodyArena=new int[BodySlabWords];mailBodyUsed=0;}
        Array.Copy(words,0,mailBodyArena,mailBodyUsed,words.Length);mail.Body=mailBodyArena;mail.BodyOffset=mailBodyUsed;mail.BodyWords=words.Length;mailBodyUsed+=words.Length;
    }
    private void WriteMail(JobWireWriter w)
    {
        int count=0;for(int i=0;i<Mail.Length;i++)if(Mail[i].State!=JobMailState.Empty)count++;w.Int(count);
        for(int i=0;i<Mail.Length;i++)if(Mail[i].State!=JobMailState.Empty){var m=Mail[i];w.Int(i);w.Key(m.Id);w.Key(m.Owner);w.Key(m.Sender);w.Key(m.Recipient);w.Key(m.Reservation);w.Int((int)m.State);w.Int(m.Gift);w.Int(m.ReplyText);w.Int(m.Read?1:0);w.Int(m.Reply?1:0);w.Long(m.SentAt);w.Long(m.DueAt);w.Block(m.Body,m.BodyOffset,m.BodyWords);}
    }
    private JobMailRecord ReadbackBody(int slot,JobIdentity id)
    {if(readbackMemo==null)return default;if(slot<readbackMemo.Mail.Length&&readbackMemo.Mail[slot].Id==id)return readbackMemo.Mail[slot];for(int i=0;i<readbackMemo.Mail.Length;i++)if(readbackMemo.Mail[i].Id==id)return readbackMemo.Mail[i];return default;}
    private static bool SameEnvelope(in JobMailRecord a,in JobMailRecord b)=>a.Id==b.Id&&a.Owner==b.Owner&&a.Sender==b.Sender&&a.Recipient==b.Recipient&&a.Reservation==b.Reservation&&a.State==b.State&&a.Gift==b.Gift&&a.ReplyText==b.ReplyText&&a.Read==b.Read&&a.Reply==b.Reply&&a.SentAt==b.SentAt&&a.DueAt==b.DueAt&&a.Body==b.Body&&a.BodyOffset==b.BodyOffset&&a.BodyWords==b.BodyWords;
    private void ReadMail(JobWireReader r)
    {
        int count=r.Range(0,InboxCount+OutboxCapacity);if(count>0)EnsureMail();
        // Validate every envelope/body, but share identical metadata pages with
        // the source snapshot. Either owner copies a page before changing it.
        bool[]? seen=null;
        if(count>0&&readbackMemo!=null&&readbackMemo.TownId==TownId&&readbackMemo.Mail.Length==Mail.Length){Mail=readbackMemo.Mail.Share();seen=new bool[Mail.Length];}
        for(int n=0;n<count;n++){
            int at=r.Range(0,Mail.Length-1);if(seen!=null?seen[at]:Mail[at].State!=JobMailState.Empty)throw new InvalidOperationException("Duplicate saved letter slot");if(seen!=null)seen[at]=true;
            var m=new JobMailRecord{Id=r.Key(false),Owner=r.Key(false),Sender=r.Key(false),Recipient=r.Key(false),Reservation=r.Key(),State=(JobMailState)r.Range(1,2),Gift=r.Range(0,Catalog.Items.Length-1),ReplyText=r.Range(0,255),Read=r.Range(0,1)==1,Reply=r.Range(0,1)==1,SentAt=r.Long(),DueAt=r.Long(),Body=null};
            var prior=ReadbackBody(at,m.Id);m.Body=r.Block(1001,prior.Body,prior.BodyOffset,prior.BodyWords,out m.BodyOffset,out m.BodyWords);
            if((m.State==JobMailState.Inbox)!=(at<InboxCount)||m.DueAt<m.SentAt||!m.Reply&&m.Body==null)throw new InvalidOperationException("Invalid saved letter envelope");
            if(m.Body!=null){
                JobTextBody.Validate(m.Body,m.BodyOffset,m.BodyWords);
                if(m.Body!=prior.Body){
                    var compact=JobTextBody.Compact(m.Body);bool same=prior.Body!=null&&prior.BodyWords==compact.Length;
                    for(int i=0;same&&i<compact.Length;i++)if(compact[i]!=prior.Body![prior.BodyOffset+i])same=false;
                    if(same){m.Body=prior.Body;m.BodyOffset=prior.BodyOffset;m.BodyWords=prior.BodyWords;}
                    else StoreBody(ref m,compact);
                }
            }if(seen==null||!SameEnvelope(m,Mail[at]))Mail.Set(at,m);
        }
        if(seen!=null)for(int i=0;i<Mail.Length;i++)if(!seen[i]&&Mail[i].State!=JobMailState.Empty)Mail.Set(i,default);
    }
}
