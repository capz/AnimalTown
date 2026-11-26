using System.Net.WebSockets;
using AnimalTown.Online;

static class HostedVisitChecks
{
    public static async Task Run(ClientWebSocket owner,ClientWebSocket first,ClientWebSocket second,uint epoch,Action<bool,string> check)
    {
        using var host=new Client(owner,epoch,0);using var guest=new Client(first,epoch,1);using var other=new Client(second,epoch,0);
        foreach(var player in new[]{host,guest,other})
        {
            var result=await player.Command(new(){Kind=OnlineCommandKind.Snapshot});check(result.Status==200,"A participant can request a recipient snapshot");
            for(int i=0;i<100&&player.State.NeedsSnapshot;i++)await player.Receive();
            check(player.State.World!=null&&!player.State.NeedsSnapshot&&player.State.Wallet==4356&&player.State.Pockets[2]==AnimalTown.Catalog.WritingPaper,
                "Paged public world and selected private inventory load through the real gameplay path");
        }
        var forged=await guest.Command(new(){Kind=OnlineCommandKind.EnterRoom,A=7});
        check(forged.Status==409,"Guessed room IDs do not teleport a guest to a shop");
        var entered=await host.Command(new(){Kind=OnlineCommandKind.EnterRoom,A=7});
        check(entered.Status==200,"A nearby human enters the authoritative shop room");
        var message=new OnlineCommand{Kind=OnlineCommandKind.Chat,Text="hello from outdoors"};
        var sent=await guest.Command(message);var resent=await guest.Command(message);
        check(sent.Status==200&&resent.Status==200&&sent.Revision==resent.Revision,"Chat commit-before-reply retry has one event identity");
        for(int i=0;i<50&&host.State.ChatCount==0;i++)await host.Receive();
        check(host.State.ChatCount==1&&host.State.ChatAt(0).Text==message.Text&&host.State.ChatAt(0).Name=="GuestA",
            "Authenticated chat crosses rooms without replaying duplicate lines");
        var resident=guest.State.World!.Town.Jobs.Npc(0);
        var mail=new OnlineCommand{Kind=OnlineCommandKind.SendMail,Entity=new(resident.Town,resident.Serial),A=2,B=-1,
            Text="Hello! Your town has been a lovely place to visit. Thank you for showing me around. I hope we can talk again soon."};
        var mailed=await guest.Command(mail);var mailRetry=await guest.Command(mail);
        check(mailed.Status==200&&mailRetry.Status==200&&mailed.Revision==mailRetry.Revision,"Resident text mail is scheduled exactly once across command retries");
        var attached=await guest.Command(new(){Kind=OnlineCommandKind.SendMail,Entity=mail.Entity,A=2,B=3,Text=mail.Text});
        check(attached.Status==400,"Online mail rejects attachments before consuming stationery");
        var stranger=await other.Command(new(){Kind=OnlineCommandKind.SendMail,Entity=new(7,17),A=2,B=-1,Text=mail.Text});
        check(stranger.Status==403,"Online mail rejects identities outside the visiting town");
    }
    internal readonly record struct Reply(int Status,long Revision,string Error);
    sealed class Client(ClientWebSocket socket,uint epoch,uint sequence):IDisposable
    {
        public readonly OnlineVisitState State=new();readonly byte[] result=new byte[512];long sentAt;
        public async Task<Reply> Command(OnlineCommand command)
        {
            long wait=300-(Environment.TickCount64-sentAt);if(wait>0)await Task.Delay((int)wait);
            sentAt=Environment.TickCount64;
            if(command.Id.IsEmpty){var id=Guid.NewGuid().ToByteArray();command.Id=new OnlinePacketReader(id,0,16).Id();}
            var payload=new byte[4096];int length=OnlineGameplayCodec.WriteCommand(payload,command);
            var frame=new byte[12+length];OnlineWire.TryWriteHeader(frame,0,frame.Length,new(length>512?OnlineFrameType.BulkPage:OnlineFrameType.Command,length,epoch,++sequence));Array.Copy(payload,0,frame,12,length);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await socket.SendAsync(frame,WebSocketMessageType.Binary,true,deadline.Token);
            for(int i=0;i<150;i++)
            {
                await Receive();
                if(!State.TakeResult(result,out int count))continue;
                var r=new OnlinePacketReader(result,0,count);if(r.Byte()!=OnlineGameplayCodec.Result||!r.Id().Same(command.Id))throw new Exception("Command result identity mismatch");
                int status=r.Int();long revision=r.Long();string error=r.Text(128);return new(status,revision,error);
            }
            throw new Exception("Command result did not arrive");
        }
        public async Task Receive()
        {
            var bytes=new byte[4108];int at=0;
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while(true)
            {
                var message=await socket.ReceiveAsync(bytes.AsMemory(at),deadline.Token);
                if(message.MessageType!=WebSocketMessageType.Binary||message.Count==0)throw new Exception("Online fixture connection closed");
                at+=message.Count;if(!message.EndOfMessage)continue;
                if(OnlineWire.ReadFrame(bytes,0,at,out var header,out int consumed)!=OnlineFrameRead.Complete||consumed!=at)throw new Exception("Invalid hosted frame");
                if(header.Type is OnlineFrameType.ReliableEvent or OnlineFrameType.SnapshotPage or OnlineFrameType.BulkPage)
                    if(!State.Apply(header,bytes,12,header.PayloadLength))throw new Exception("Hosted projection parser rejected a server frame");
                return;
            }
        }
        public void Dispose()=>State.Dispose();
    }
}
