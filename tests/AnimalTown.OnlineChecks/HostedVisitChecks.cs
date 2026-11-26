using System.Net.WebSockets;
using AnimalTown.Online;

static class HostedVisitChecks
{
    public static async Task CheckNewMembership(ClientWebSocket socket,uint epoch,Action<bool,string> check)
    {
        using var current=new Client(socket,epoch,0,2);current.StartHeartbeat();
        check((await current.Command(new(){Kind=OnlineCommandKind.Replay,Revision=0})).Status==200,"A new visit may request replay with a forged old cursor");
        long deadline=Environment.TickCount64+2200;
        while(Environment.TickCount64<deadline)await current.Receive();
        check(current.State.ChatCount==0,"A new membership cannot read chat sent before it joined, even with a forged replay cursor");
    }
    public static async Task Run(ClientWebSocket owner,ClientWebSocket first,ClientWebSocket second,uint epoch,Action<bool,string> check)
    {
        using var host=new Client(owner,epoch,0,0);using var guest=new Client(first,epoch,1,1);using var other=new Client(second,epoch,0,2);
        host.StartHeartbeat();guest.StartHeartbeat();other.StartHeartbeat();
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
        await guest.WalkToResident(0);
        var offered=await guest.Command(new(){Kind=OnlineCommandKind.AskJob,Entity=new(resident.Town,resident.Serial),A=0});
        var npcPose=guest.State.World.Town.Residents[0];
        check(offered.Status==200,"Guest job offer: "+offered.Status+" "+offered.Error+"; human="+guest.Position.Room+","+guest.Position.X/65536f+","+guest.Position.Z/65536f+"; resident="+npcPose.Space+","+npcPose.PosX+","+npcPose.PosZ);
        var offerData=new OnlinePacketReader(offered.Extra,0,offered.Extra.Length);var jobId=offerData.Id();
        offerData.Int();offerData.Int();int template=offerData.Int();
        check(template==(int)AnimalTown.Jobs.JobTemplate.FavorLetter,"The offered letter favor needs only an enabled PoC action");
        var accept=new OnlineCommand{Kind=OnlineCommandKind.AcceptJob,Entity=jobId,Revision=offered.Revision};
        var accepted=await guest.Command(accept);var retryAccept=await guest.Command(accept);
        check(accepted.Status==200&&retryAccept.Status==200&&accepted.Revision==retryAccept.Revision,"A guest job acceptance is durable and idempotent");
        var stolen=await other.Command(new(){Kind=OnlineCommandKind.AcceptJob,Entity=jobId,Revision=offered.Revision});
        check(stolen.Status==404,"A second human cannot claim another guest's personal job context");
        var mail=new OnlineCommand{Kind=OnlineCommandKind.SendMail,Entity=new(resident.Town,resident.Serial),A=2,B=-1,
            Text="Hello! Your town has been a lovely place to visit. Thank you for showing me around. I hope we can talk again soon."};
        var mailed=await guest.Command(mail);var mailRetry=await guest.Command(mail);
        check(mailed.Status==200&&mailRetry.Status==200&&mailed.Revision==mailRetry.Revision,"Resident text mail is scheduled exactly once across command retries");
        var attached=await guest.Command(new(){Kind=OnlineCommandKind.SendMail,Entity=mail.Entity,A=2,B=3,Text=mail.Text});
        check(attached.Status==400,"Online mail rejects attachments before consuming stationery");
        var stranger=await other.Command(new(){Kind=OnlineCommandKind.SendMail,Entity=new(7,17),A=2,B=-1,Text=mail.Text});
        check(stranger.Status==403,"Online mail rejects identities outside the visiting town");
        await HostedClockChecks.Deliver(mail.Id,jobId,check);
        await guest.Snapshot();
        await guest.WalkToPoint(13.5f,24.5f);
        var world=guest.State.World!;int placeX=-1,placeZ=-1;
        for(int z=(int)(guest.Position.Z/65536f)-2;z<=(int)(guest.Position.Z/65536f)+2&&placeX<0;z++)
            for(int x=(int)(guest.Position.X/65536f)-2;x<=(int)(guest.Position.X/65536f)+2;x++)
                if(MathF.Abs(x+.5f-guest.Position.X/65536f)<=1.5f&&MathF.Abs(z+.5f-guest.Position.Z/65536f)<=1.5f&&world.CanPlace(10,x,z,0,0,0)){placeX=x;placeZ=z;break;}
        check(placeX>=0,"The fixture has a reachable legal furnishing footprint");
        var placed=await guest.Command(new(){Kind=OnlineCommandKind.Place,Revision=guest.State.InventoryRevision,A=0,B=3,C=placeX,D=placeZ,E=0,F=0});
        check(placed.Status==200,"A guest placement reserves the selected home inventory item");
        var placementData=new OnlinePacketReader(placed.Extra,0,placed.Extra.Length);var placement=placementData.Id();int version=placementData.Int();
        var review=await host.Command(new(){Kind=OnlineCommandKind.ReviewBegin});check(review.Status==200,"The owner begins durable review through the authenticated gameplay channel");
        var lease=new OnlinePacketReader(review.Extra,0,review.Extra.Length).Id();
        while(host.State.NeedsSnapshot)await host.Receive();
        var moved=await guest.Command(new(){Kind=OnlineCommandKind.MovePlacement,Entity=placement,A=version,C=placeX,D=placeZ,E=1,F=0});
        check(moved.Status==409,"A forged guest edit is rejected during owner review");
        var leaseBytes=new byte[16];new OnlinePacketWriter(leaseBytes).Id(lease);string leaseText=new Guid(leaseBytes).ToString();
        var decision=new OnlineCommand{Kind=OnlineCommandKind.ReviewDecision,Entity=placement,Revision=version,A=0,Text=leaseText};
        var kept=await host.Command(decision);var repeated=await host.Command(decision);
        check(kept.Status==200&&repeated.Status==200,"Keep commits once and retries return the same durable decision");
        check((await host.Command(new(){Kind=OnlineCommandKind.ReviewEnd,Entity=lease})).Status==200,"Ending owner review resumes the active visit");
    }
    internal readonly record struct Reply(int Status,long Revision,string Error,byte[] Extra);
    sealed class Client(ClientWebSocket socket,uint epoch,uint sequence,byte localSlot):IDisposable
    {
        public readonly OnlineVisitState State=new();readonly byte[] result=new byte[512];long sentAt;
        public OnlineActorPosition Position;
        readonly SemaphoreSlim sending=new(1);readonly CancellationTokenSource heartbeatStop=new();Task? heartbeat;
        public void StartHeartbeat()=>heartbeat=Beat();
        async Task Beat()
        {
            try
            {
                while(true)
                {
                    await Task.Delay(4000,heartbeatStop.Token);
                    var frame=new byte[12];OnlineWire.TryWriteHeader(frame,0,12,new(OnlineFrameType.Heartbeat,0,epoch,0));
                    await Send(frame,heartbeatStop.Token);
                }
            }
            catch(OperationCanceledException)when(heartbeatStop.IsCancellationRequested){}
            catch(WebSocketException){}
        }
        async Task Send(byte[] frame,CancellationToken token)
        {
            await sending.WaitAsync(token);
            try{await socket.SendAsync(frame,WebSocketMessageType.Binary,true,token);}finally{sending.Release();}
        }
        public async Task<Reply> Command(OnlineCommand command)
        {
            long wait=300-(Environment.TickCount64-sentAt);if(wait>0)await Task.Delay((int)wait);
            sentAt=Environment.TickCount64;
            if(command.Kind is OnlineCommandKind.Snapshot or OnlineCommandKind.ReviewBegin)State.ExpectSnapshot();
            if(command.Id.IsEmpty){var id=Guid.NewGuid().ToByteArray();command.Id=new OnlinePacketReader(id,0,16).Id();}
            var payload=new byte[4096];int length=OnlineGameplayCodec.WriteCommand(payload,command);
            var frame=new byte[12+length];OnlineWire.TryWriteHeader(frame,0,frame.Length,new(length>512?OnlineFrameType.BulkPage:OnlineFrameType.Command,length,epoch,++sequence));Array.Copy(payload,0,frame,12,length);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await Send(frame,deadline.Token);
            long replyDeadline=Environment.TickCount64+8000;
            for(int i=0;i<10000&&Environment.TickCount64<replyDeadline;i++)
            {
                await Receive();
                if(!State.TakeResult(result,out int count))continue;
                var r=new OnlinePacketReader(result,0,count);if(r.Byte()!=OnlineGameplayCodec.Result||!r.Id().Same(command.Id))throw new Exception("Command result identity mismatch");
                int status=r.Int();long revision=r.Long();string error=r.Text(128);var extra=new byte[r.Remaining];r.Bytes(extra,0,extra.Length);return new(status,revision,error,extra);
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
                if(header.Type==OnlineFrameType.ActorPosition&&OnlineWire.TryReadPosition(bytes,0,at,out _,out var pose)&&pose.Slot==localSlot)Position=pose;
                if(header.Type is OnlineFrameType.ReliableEvent or OnlineFrameType.SnapshotPage or OnlineFrameType.BulkPage)
                    if(!State.Apply(header,bytes,12,header.PayloadLength))throw new Exception("Hosted projection parser rejected a server frame");
                return;
            }
        }
        public async Task WalkToResident(int npc)
        {
            var world=State.World!;long deadline=Environment.TickCount64+30000;
            while(Environment.TickCount64<deadline)
            {
                var target=world.Town.Residents[npc];float x=Position.X/65536f,z=Position.Z/65536f;
                if(Position.Room!=target.Space)throw new Exception("Fixture resident left the supported approach room");
                float dx=target.PosX-x,dz=target.PosZ-z;
                if(dx*dx+dz*dz<2.25f){await Move(default);return;}
                var step=NextStep(world,x,z,target.PosX,target.PosZ);
                dx=step.X-x;dz=step.Z-z;float scale=MathF.Max(.0001f,MathF.Sqrt(dx*dx+dz*dz));
                await Move(new((sbyte)Math.Clamp((int)(dx/scale*127),-127,127),(sbyte)Math.Clamp((int)(dz/scale*127),-127,127),true));
            }
            throw new Exception("Authenticated movement could not reach the fixture resident");
        }
        public async Task WalkToPoint(float targetX,float targetZ)
        {
            var world=State.World!;long deadline=Environment.TickCount64+30000;
            while(Environment.TickCount64<deadline)
            {
                float x=Position.X/65536f,z=Position.Z/65536f;
                float dx=targetX-x,dz=targetZ-z;
                if(dx*dx+dz*dz<.64f){await Move(default);return;}
                var step=NextStep(world,x,z,targetX,targetZ);dx=step.X-x;dz=step.Z-z;
                float scale=MathF.Max(.0001f,MathF.Sqrt(dx*dx+dz*dz));
                await Move(new((sbyte)Math.Clamp((int)(dx/scale*127),-127,127),(sbyte)Math.Clamp((int)(dz/scale*127),-127,127),true));
            }
            throw new Exception("Authenticated movement could not reach the fixture point");
        }
        public async Task Snapshot()
        {
            var reply=await Command(new(){Kind=OnlineCommandKind.Snapshot});if(reply.Status!=200)throw new Exception("Snapshot request failed");
            while(State.NeedsSnapshot)await Receive();
        }
        async Task Move(OnlineMovementInput input)
        {
            var frame=new byte[15];uint sample=++sequence;OnlineWire.TryWriteInput(frame,0,15,epoch,sample,input);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));await Send(frame,deadline.Token);
            for(int i=0;i<100;i++){await Receive();if(Position.AcknowledgedInput>=sample)return;}
            throw new Exception("Authoritative movement acknowledgment did not arrive");
        }
        static (float X,float Z) NextStep(AnimalTown.GameState world,float x,float z,float targetX,float targetZ)
        {
            if(AnimalTown.BuildingWorld.Segment(world,0,x,z,targetX,targetZ))return(targetX,targetZ);
            int width=AnimalTown.Town.Width,cells=AnimalTown.Town.Cells;
            int start=(int)z*width+(int)x,goal=(int)targetZ*width+(int)targetX;
            var previous=new int[cells];Array.Fill(previous,-1);var queue=new int[cells];int read=0,write=1;queue[0]=start;previous[start]=start;
            int[] offsets={-1,1,-width,width};
            while(read<write&&previous[goal]<0)
            {
                int cell=queue[read++];
                foreach(int delta in offsets)
                {
                    int next=cell+delta;
                    if(next<0||next>=cells||previous[next]>=0||Math.Abs(next%width-cell%width)+Math.Abs(next/width-cell/width)!=1)continue;
                    if(!AnimalTown.BuildingWorld.Segment(world,0,cell%width+.5f,cell/width+.5f,next%width+.5f,next/width+.5f))continue;
                    previous[next]=cell;queue[write++]=next;
                }
            }
            if(previous[goal]<0)throw new Exception("Fixture has no legal route to its resident");
            int at=goal;while(previous[at]!=start&&at!=start)at=previous[at];
            return(at%width+.5f,at/width+.5f);
        }
        public void Dispose()
        {
            heartbeatStop.Cancel();try{heartbeat?.GetAwaiter().GetResult();}catch(OperationCanceledException){}
            heartbeatStop.Dispose();sending.Dispose();State.Dispose();
        }
    }
}
