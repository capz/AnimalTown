using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;
using AnimalTown.Online;

static class VisitChecks
{
    sealed record Player(string Id,string Account,string Device,string Fingerprint);
    public static async Task Run(string origin,Action<bool,string> check)
    {
        using var api=new HttpClient { BaseAddress=new Uri(origin),Timeout=TimeSpan.FromSeconds(15) };
        string suffix=Guid.NewGuid().ToString("N")[..7];
        async Task<JsonElement> Request(HttpMethod method,string path,object? body=null,string? token=null)
        {
            using var request=new HttpRequestMessage(method,path);
            if(token is not null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            if(body is not null)request.Content=JsonContent.Create(body);
            using var response=await api.SendAsync(request);
            if(!response.IsSuccessStatusCode)throw new Exception($"Fixture request {path} returned {(int)response.StatusCode}");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }
        async Task<Player> Create(string role,int seed)
        {
            var account=await Request(HttpMethod.Post,"/v1/auth/register",new{name=role+suffix,password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24))});
            string id=account.GetProperty("id").GetString()!,browser=account.GetProperty("token").GetString()!;
            ulong townId=BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));if(townId==0)townId=1;
            var state=new GameState(seed){Name=role,Sequence=11,Wallet=4356,Savings=9021,Tutorial=7};
            state.ResidentJobs.Enable(townId,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            state.Rollover((int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds()/86400),DateTime.UtcNow.Year*12+DateTime.UtcNow.Month-1);
            state.Pockets[2]=Catalog.WritingPaper;state.Pockets[3]=10;
            state.Pockets[4]=Catalog.Apple;state.Pockets[5]=Catalog.Pear;
            if(role=="Owner"){
                if(!state.PlaceHouse(12,22,false,true))throw new Exception("Fixture home plot is blocked");
                state.X=32.5f;state.Z=28.5f;
                if(!HelipadSite.TryArrival(state,out float arrivalX,out float arrivalZ))throw new Exception("Fixture arrival is blocked");
                for(int npc=0;npc<3;npc++)
                {
                    var resident=state.Town.Residents[npc];resident.Space=0;resident.PosX=arrivalX;resident.PosZ=arrivalZ;
                    resident.X=(int)arrivalX;resident.Z=(int)arrivalZ;resident.JourneyPhase=0;resident.Dwell=36000;
                }
            }
            var start=await Request(HttpMethod.Post,"/v1/auth/link/start",new{town=townId.ToString("X16")});
            await Request(HttpMethod.Post,"/v1/account/link",new{code=start.GetProperty("code").GetString()},browser);
            var linked=await Request(HttpMethod.Post,"/v1/auth/link/poll",new{secret=start.GetProperty("secret").GetString()});
            string device=linked.GetProperty("token").GetString()!;
            using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
            {
                byte[][] records=SplitSaveCodec.Encode(state);byte[] commit=SplitSaveCodec.EncodeCommit(state,records);
                writer.Write(commit.Length);writer.Write(commit);foreach(byte[] record in records){writer.Write(record.Length);writer.Write(record);}
            }
            byte[] checkpoint=bytes.ToArray();
            var offer=await Request(HttpMethod.Post,$"/v1/towns/{id}/checkpoint-uploads",new{length=checkpoint.Length,sha256=Convert.ToHexString(SHA256.HashData(checkpoint))},device);
            for(int offset=0;offset<checkpoint.Length;offset+=16384)
            {
                using var upload=new HttpRequestMessage(HttpMethod.Put,$"/v1/towns/{id}/checkpoint-uploads/{offer.GetProperty("id").GetString()}/page?offset={offset}");
                upload.Headers.Authorization=new AuthenticationHeaderValue("Bearer",device);upload.Content=new ByteArrayContent(checkpoint.AsSpan(offset,Math.Min(16384,checkpoint.Length-offset)).ToArray());upload.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");
                using var response=await api.SendAsync(upload);check(response.IsSuccessStatusCode,"Existing split-save import through SDK-sized complete HTTP pages");
            }
            var card=await Request(HttpMethod.Get,"/v1/online/card",token:device);
            check(card.GetProperty("revision").GetInt64()==1&&card.GetProperty("sha256").GetString()==Convert.ToHexString(SHA256.HashData(checkpoint)),"Device control reports the exact acknowledged card, without public or canonical private data");
            var saved=await Request(HttpMethod.Get,"/v1/me",token:device);
            check(saved.GetProperty("wallet").GetInt32()==4356&&saved.GetProperty("savings").GetInt32()==9021,"Import preserves the home economy");
            return new Player(id,browser,device,Convert.ToHexString(SHA256.HashData(checkpoint)));
        }
        Player owner=await Create("Owner",1838),first=await Create("GuestA",1860),second=await Create("GuestB",1861);
        foreach(var guest in new[]{first,second})
        {
            await Request(HttpMethod.Post,$"/v1/friends/{guest.Id}",new{},owner.Account);
            await Request(HttpMethod.Post,$"/v1/friends/{owner.Id}",new{},guest.Account);
        }
        await Request(HttpMethod.Post,"/v1/online/gate/open",new{},owner.Device);
        var friends=await Request(HttpMethod.Get,"/v1/online/friends?page=0",token:first.Device);
        check(friends.GetProperty("friends").EnumerateArray().Any(f=>f.GetProperty("id").GetString()==owner.Id),"Open-towns UI discovers only admitted mutual friends");
        var visitA=await Request(HttpMethod.Post,"/v1/online/visits",new{host=owner.Id},first.Device);
        var visitB=await Request(HttpMethod.Post,"/v1/online/visits",new{host=owner.Id},second.Device);
        string reservationA=Guid.NewGuid().ToString();
        foreach(var entry in new[]{(owner,Guid.Empty.ToString(),Guid.NewGuid().ToString()),(first,visitA.GetProperty("id").GetString()!,reservationA),(second,visitB.GetProperty("id").GetString()!,Guid.NewGuid().ToString())})
        {
            await Request(HttpMethod.Post,"/v1/online/reservations",new{id=entry.Item3,host=owner.Id,visit=entry.Item2,revision=1,fingerprint=entry.Item1.Fingerprint,profile=0},entry.Item1.Device);
            await Request(HttpMethod.Post,$"/v1/online/reservations/{entry.Item3}/activate",new{},entry.Item1.Device);
        }
        var websocket=new UriBuilder(origin){Scheme=origin.StartsWith("https://")?"wss":"ws",Path="/v1/gameplay"}.Uri;
        async Task<ClientWebSocket> Connect(byte[] ticket,string protocol="animaltown.v1")
        {
            var socket=new ClientWebSocket();socket.Options.AddSubProtocol(protocol);
            try
            {
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(websocket,deadline.Token);
                var frame=new byte[44];OnlineWire.TryWriteHeader(frame,0,44,new OnlineFrameHeader(OnlineFrameType.GameplayTicket,32,0,0));ticket.CopyTo(frame,12);
                // Exercise a fragmented message through the real server stream adapter.
                await socket.SendAsync(new ArraySegment<byte>(frame,0,5),WebSocketMessageType.Binary,false,deadline.Token);
                await socket.SendAsync(new ArraySegment<byte>(frame,5,39),WebSocketMessageType.Binary,true,deadline.Token);
                return socket;
            }catch{socket.Dispose();throw;}
        }
        async Task<byte[]> Ticket(Player player,string visit)
        {
            var result=await Request(HttpMethod.Post,"/v1/gameplay/tickets",new{host=owner.Id,visit},player.Device);
            return Convert.FromHexString(result.GetProperty("ticket").GetString()!);
        }
        byte[] ownerTicket=await Ticket(owner,Guid.Empty.ToString());
        using var host=await Connect(ownerTicket);
        var hostHello=await Read(host);check(hostHello.Header.Type==OnlineFrameType.Pong&&hostHello.Header.Sequence==0,"Owner gets the local seat through WSS");
        using var guestA=await Connect(await Ticket(first,visitA.GetProperty("id").GetString()!));
        var firstHello=await Read(guestA);
        using var guestB=await Connect(await Ticket(second,visitB.GetProperty("id").GetString()!));
        var secondHello=await Read(guestB);
        check(firstHello.Header.Sequence==1&&secondHello.Header.Sequence==2&&firstHello.Header.Epoch==hostHello.Header.Epoch&&secondHello.Header.Epoch==hostHello.Header.Epoch,"Three humans share one epoch and distinct seats");
        var movement=new byte[15];OnlineWire.TryWriteInput(movement,0,15,hostHello.Header.Epoch,1,new OnlineMovementInput(127,0,false));
        using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)))await guestA.SendAsync(movement,WebSocketMessageType.Binary,true,deadline.Token);
        bool moved=false;
        for(int i=0;i<40&&!moved;i++)
        {
            var next=await Read(host);
            moved=OnlineWire.TryReadPosition(next.Frame,0,next.Frame.Length,out _,out var position)&&position.Slot==1&&position.AcknowledgedInput==1;
        }
        check(moved,"The authoritative movement result reaches another human through WSS");
        uint activeEpoch=hostHello.Header.Epoch;
        var restartProof=await HostedVisitChecks.Run(host,guestA,guestB,activeEpoch,check);
        if(await BackendRestartChecks.Restart(check))
        {
            async Task<(ClientWebSocket Socket,OnlineFrameHeader Header)> Resume(Player player,string membership)
            {
                long deadline=Environment.TickCount64+30000;string failure="";
                while(Environment.TickCount64<deadline)
                {
                    ClientWebSocket? socket=null;
                    try{socket=await Connect(await Ticket(player,membership));var hello=await Read(socket);return(socket,hello.Header);}
                    catch(Exception error){socket?.Dispose();failure=error.GetType().Name;await Task.Delay(750);}
                }
                throw new Exception("Restarted backend did not resume the existing membership: "+failure);
            }
            var resumedHost=await Resume(owner,Guid.Empty.ToString());using var nextHost=resumedHost.Socket;
            var resumedFirst=await Resume(first,visitA.GetProperty("id").GetString()!);using var nextFirst=resumedFirst.Socket;
            var resumedSecond=await Resume(second,visitB.GetProperty("id").GetString()!);using var nextSecond=resumedSecond.Socket;
            check(resumedHost.Header.Epoch>activeEpoch&&resumedFirst.Header.Epoch==resumedHost.Header.Epoch&&resumedSecond.Header.Epoch==resumedHost.Header.Epoch&&
                resumedHost.Header.Sequence==0&&resumedFirst.Header.Sequence==1&&resumedSecond.Header.Sequence==2,"A restarted writer advances its epoch and resumes all three durable seats");
            activeEpoch=resumedHost.Header.Epoch;
            await HostedVisitChecks.AfterRestart(nextHost,nextFirst,nextSecond,activeEpoch,restartProof,check);
        }
        await Request(HttpMethod.Post,$"/v1/online/visits/{visitB.GetProperty("id").GetString()}/end",new{},second.Device);
        var newVisit=await Request(HttpMethod.Post,"/v1/online/visits",new{host=owner.Id},second.Device);
        using(var fresh=await Connect(await Ticket(second,newVisit.GetProperty("id").GetString()!)))
        {
            var hello=await Read(fresh);
            await HostedVisitChecks.CheckNewMembership(fresh,hello.Header.Epoch,check);
        }
        using(var replay=await Connect(ownerTicket))check(await Ended(replay),"A consumed gameplay ticket cannot authenticate a second socket");
        bool protocolRejected=false;try{using var wrong=await Connect(await Ticket(first,visitA.GetProperty("id").GetString()!),"wrong.protocol");}catch(WebSocketException){protocolRejected=true;}
        check(protocolRejected,"An unsupported WebSocket subprotocol is rejected before upgrade");
        int expectedWallet=(await Request(HttpMethod.Get,"/v1/me",token:first.Device)).GetProperty("wallet").GetInt32();
        using(var resumed=await Connect(await Ticket(first,visitA.GetProperty("id").GetString()!)))
        {
            var hello=await Read(resumed);check(hello.Header.Sequence==1&&hello.Header.Epoch==activeEpoch,"A fresh ticket resumes the reserved guest seat");
            await Request(HttpMethod.Delete,"/v1/online/gate",token:owner.Device);
            check(await Ended(resumed),"Gate close revokes an existing WSS guest connection");
        }
        var publication=await Request(HttpMethod.Post,$"/v1/online/reservations/{reservationA}/settlement",new{},first.Device);
        var retryPublication=await Request(HttpMethod.Post,$"/v1/online/reservations/{reservationA}/settlement",new{},first.Device);
        check(publication.GetProperty("sha256").GetString()==retryPublication.GetProperty("sha256").GetString(),"A return retry stages the same immutable checkpoint");
        using var returnedBytes=new MemoryStream();int publishedLength=publication.GetProperty("length").GetInt32();
        while(returnedBytes.Length<publishedLength)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,$"/v1/online/reservations/{reservationA}/checkpoint?offset={returnedBytes.Length}&count=16384");
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",first.Device);
            using var response=await api.SendAsync(request);response.EnsureSuccessStatusCode();returnedBytes.Write(await response.Content.ReadAsByteArrayAsync());
        }
        byte[] returned=returnedBytes.ToArray();
        check(Convert.ToHexString(SHA256.HashData(returned))==publication.GetProperty("sha256").GetString(),"Bounded return pages verify against the committed checkpoint fingerprint");
        using(var download=new OnlineCheckpointDownload(returned.Length,publication.GetProperty("sha256").GetString()!))
        {
            for(int at=0;at<returned.Length;at+=31)download.Append(returned,at,Math.Min(31,returned.Length-at));
            download.Verify();check(download.Position==returned.Length,"Console return decoder verifies a real settlement across arbitrary record/page boundaries");
        }
        GameState returnedHome;
        using(var reader=new BinaryReader(new MemoryStream(returned)))
        {
            byte[] commit=reader.ReadBytes(reader.ReadInt32());var parts=new byte[7][];
            for(int i=0;i<parts.Length;i++)parts[i]=reader.ReadBytes(reader.ReadInt32());
            var saved=SplitSaveCodec.DecodeCommitted(commit,parts);
            returnedHome=saved;
            check(saved.Name=="GuestA"&&saved.Wallet==expectedWallet&&!saved.Pockets.Contains(Catalog.WritingPaper),"Return exports the correct home economy and consumes successful mail stationery once");
        }
        var acknowledged=await Request(HttpMethod.Post,$"/v1/online/reservations/{reservationA}/published",new{fingerprint=publication.GetProperty("sha256").GetString()},first.Device);
        var retriedAck=await Request(HttpMethod.Post,$"/v1/online/reservations/{reservationA}/published",new{fingerprint=publication.GetProperty("sha256").GetString()},first.Device);
        check(acknowledged.GetProperty("revision").GetInt64()==2&&retriedAck.GetProperty("revision").GetInt64()==2,"Publication acknowledgment advances card lineage exactly once");
        returnedHome.Wallet+=17;returnedHome.Sequence++;
        byte[] offline;
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
        {
            var records=SplitSaveCodec.Encode(returnedHome);var commit=SplitSaveCodec.EncodeCommit(returnedHome,records);
            writer.Write(commit.Length);writer.Write(commit);foreach(var record in records){writer.Write(record.Length);writer.Write(record);}offline=stream.ToArray();
        }
        string offlineHash=Convert.ToHexString(SHA256.HashData(offline));
        var newOffer=await Request(HttpMethod.Post,$"/v1/towns/{first.Id}/checkpoint-uploads",new{length=offline.Length,sha256=offlineHash,parentRevision=2},first.Device);
        using(var request=new HttpRequestMessage(HttpMethod.Put,$"/v1/towns/{first.Id}/checkpoint-uploads/{newOffer.GetProperty("id").GetString()}?offset=0"))
        {
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",first.Device);request.Content=new ByteArrayContent(offline);request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");
            using var response=await api.SendAsync(request);
            string reply=await response.Content.ReadAsStringAsync();
            check(response.IsSuccessStatusCode,"Offline progress uploads against the acknowledged online return, after an older completed upload: "+reply);
            using var result=JsonDocument.Parse(reply);
            check(result.RootElement.GetProperty("revision").GetInt64()==3,"Offline upload advances the actual acknowledged card head once");
        }
        var afterOffline=await Request(HttpMethod.Get,"/v1/me",token:first.Device);
        check(afterOffline.GetProperty("wallet").GetInt32()==expectedWallet+17,"Online purchases and independent offline earnings survive the next synchronization");
    }
    static async Task<(OnlineFrameHeader Header,byte[] Frame)> Read(ClientWebSocket socket)
    {
        var buffer=new byte[OnlineWire.HeaderSize+OnlineWire.MaxPayloadSize];int count=0;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while(true)
        {
            var part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer,count,buffer.Length-count),deadline.Token);
            if(part.MessageType!=WebSocketMessageType.Binary||part.Count==0)throw new Exception("Gameplay connection closed unexpectedly");
            count+=part.Count;if(!part.EndOfMessage){if(count==buffer.Length)throw new Exception("Unbounded gameplay response");continue;}
            if(OnlineWire.ReadFrame(buffer,0,count,out var header,out int length)!=OnlineFrameRead.Complete||length!=count)throw new Exception("Invalid gameplay response framing");
            return(header,buffer[..count]);
        }
    }
    static async Task<bool> Ended(ClientWebSocket socket)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));var buffer=new byte[512];
        try{while(true){var result=await socket.ReceiveAsync(buffer,deadline.Token);if(result.MessageType==WebSocketMessageType.Close||result.Count==0)return true;}}
        catch(WebSocketException){return true;}
        catch(OperationCanceledException){return false;}
    }
}
