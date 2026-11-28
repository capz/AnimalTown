#if ANIMALTOWN_ONLINE_PLAY
using System;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Timing;

// One reliable operation is retained until its receipt arrives. Reconnect uses
// the same operation ID; optimistic inventory edits never touch the home card.
internal sealed class OnlinePlayVisit:IDisposable
{
    internal readonly OnlineVisitState State=new OnlineVisitState();
    internal readonly OnlineId Self;
    internal readonly string Host,Visit;
    internal readonly bool Owner;
    internal readonly int Profile;
    internal OnlineGameplayConnection Connection=null!;
    internal OnlineCommand? Pending {get;private set;}
    internal bool Connected {get;private set;}=true;
    internal bool Recovering {get;private set;}
    internal bool ReturnRequested;
    internal long CredentialExpiresAt=long.MaxValue;
    internal int ControlRequested;
    internal string ChatDraft="",MailDraft="",MailRecipientName="";
    internal OnlineId MailRecipient;
    internal int MailPaper=-1;
    internal int ReplySerial,Status;
    internal long Revision;
    internal string Error="";
    internal OnlineCommandKind RepliedKind;
    internal readonly byte[] Extra=new byte[512];internal int ExtraCount;
    readonly byte[] commandBytes=new byte[4096],replyBytes=new byte[512];
    bool snapshotPending,queued,replayNeeded;uint lastAck;long poseAt,replayWatermark=-1;
    OnlineCommand? retryAfterReplay;
    internal bool Reviewing=>Connection.Reviewing;
    internal byte Slot=>Connection.LocalSlot;
    internal GameState? World=>State.World;
    internal OnlinePlayVisit(string self,string host,string visit,int profile)
    {Self=OnlineIdentifiers.Parse(self);Host=host;Visit=visit;Owner=self==host;Profile=profile;}
    internal bool Send(OnlineCommand command)
    {
        if(!Connected||Pending!=null||Connection.Epoch==0||State.NeedsSnapshot&&command.Kind!=OnlineCommandKind.Snapshot&&command.Kind!=OnlineCommandKind.Replay)return false;
        if(command.Id.IsEmpty)command.Id=Connection.NewCommandId();
        Pending=command;queued=false;return true;
    }
    internal void Reconnect(OnlineGameplayConnection connection)
    {
        Connection.Dispose();Connection=connection;Connected=true;queued=false;snapshotPending=false;lastAck=0;poseAt=0;
        retryAfterReplay=Pending?.Kind==OnlineCommandKind.Snapshot||Pending?.Kind==OnlineCommandKind.Replay?null:Pending;
        Pending=null;Recovering=true;replayNeeded=true;replayWatermark=-1;
    }
    internal void Tick(OnlineMovementInput input)
    {
        if(!Connected)return;
        if(Clock.Timestamp>=CredentialExpiresAt-30000000){Disconnect();return;}
        Connected=Connection.Tick(input);if(!Connected)return;
        if(State.TakeResult(replyBytes,out int count))
        {
            var read=new OnlinePacketReader(replyBytes,0,count);
            if(read.Byte()!=OnlineGameplayCodec.Result)throw new InvalidOperationException("Invalid gameplay receipt.");
            var id=read.Id();int status=read.Int();long revision=read.Long();string error=read.Text(128);
            if(Pending!=null&&Pending.Id.Same(id))
            {
                RepliedKind=Pending.Kind;Status=status;Revision=revision;Error=error;ExtraCount=read.Remaining;
                read.Bytes(Extra,0,ExtraCount);Pending=null;queued=false;ReplySerial++;
                if(RepliedKind==OnlineCommandKind.Replay&&Recovering)
                {replayWatermark=status==200?revision:State.Cursor;if(status!=200)State.ExpectSnapshot();}
                if(RepliedKind==OnlineCommandKind.Snapshot&&status!=200)snapshotPending=false;
            }
            Array.Clear(replyBytes,0,count);
        }
        if(!State.NeedsSnapshot)snapshotPending=false;
        if(Recovering&&replayNeeded&&Connection.Epoch!=0)
        {replayNeeded=false;Send(new OnlineCommand{Kind=OnlineCommandKind.Replay,Revision=State.Cursor});}
        bool replayComplete=!Recovering||replayWatermark>=0&&(State.Cursor>=replayWatermark||State.ReplayGap);
        if(Recovering&&replayComplete&&!State.NeedsSnapshot&&Pending==null)
        {Recovering=false;if(retryAfterReplay!=null){Pending=retryAfterReplay;retryAfterReplay=null;queued=false;}}
        if(Pending==null&&State.NeedsSnapshot&&replayComplete&&!snapshotPending&&Connection.Epoch!=0)
        {if(Send(new OnlineCommand{Kind=OnlineCommandKind.Snapshot})){snapshotPending=true;State.ExpectSnapshot();}}
        if(Pending!=null&&!queued)
        {int size=OnlineGameplayCodec.WriteCommand(commandBytes,Pending);queued=Connection.Queue(OnlineGameplayCodec.CommandFrame(size),commandBytes,0,size,out _);}
        var world=State.World;if(world==null)return;
        world.Name=State.Name;world.Species=State.Species;world.Palette=State.Palette;world.Shirt=State.Shirt;world.BackpackTier=State.Capacity/10-1;
        world.Wallet=State.Wallet;world.Savings=State.Savings;world.Debt=State.Debt;world.Tool=0;
        Array.Copy(State.Pockets,world.Pockets,world.Pockets.Length);
        if(Connection.TryGetActor(Slot,out var pose))
        {
            long now=Clock.Timestamp;
            if(poseAt==0||pose.Room!=world.Space||pose.AcknowledgedInput!=lastAck)
            {
                float x=pose.X/65536f,z=pose.Z/65536f,dx=x-world.X,dz=z-world.Z;
                // Predict immediately, then correct new authoritative samples.
                // A large error or a portal snaps; small drift blends quickly.
                bool snap=poseAt==0||pose.Room!=world.Space||dx*dx+dz*dz>2.25f;
                world.X=snap?x:world.X+dx*.35f;world.Z=snap?z:world.Z+dz*.35f;world.Space=pose.Room;
                if(snap)world.Facing=pose.Facing*(6.2831853f/65536f);
                lastAck=pose.AcknowledgedInput;poseAt=now;
            }
        }
    }
    internal void Disconnect(){Connected=false;Connection?.Dispose();}
    public void Dispose(){Disconnect();State.Dispose();Array.Clear(commandBytes,0,commandBytes.Length);Array.Clear(replyBytes,0,replyBytes.Length);Array.Clear(Extra,0,Extra.Length);}
}
#endif
