using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;
using AnimalTown.Online;
using Npgsql;

// Disposable accounts and private card inputs for the real PPC client. Only
// redacted observations/captures leave CI; tokens and raw cards never ship.
static class DolphinTargetFixture
{
    sealed class Disk:ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=[];
        public byte[] Read(string name)=>Files.TryGetValue(name,out var b)?(byte[])b.Clone():throw new FileNotFoundException();
        public void Write(string name,byte[] bytes)=>Files[name]=(byte[])bytes.Clone();
    }
    sealed record Player(Guid Id,string Browser,Guid Device,string Town,Disk Card)
    {
        public string Access="",Refresh="";public DateTime RenewAt;public Guid Visit;public HostedVisitChecks.Client? Client;public ClientWebSocket? Socket;
        public CancellationTokenSource? ReadingStop;public Task? Reading;public volatile bool SawConsoleChat;
    }
    static void WriteJson(string path,object value)
    {File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value));File.Move(path+".tmp",path,true);}
    static byte[] Envelope(Disk disk)
    {
        var repository=new SaveRepository(disk);using var export=repository.OpenCheckpointExport(repository.Load()!);using var result=new MemoryStream();
        var page=new byte[16384];int count;while((count=export.Read(page,0,page.Length))>0)result.Write(page,0,count);return result.ToArray();
    }
    static async Task Stop(Player player)
    {
        player.ReadingStop?.Cancel();player.Socket?.Abort();
        if(player.Reading!=null)try{await player.Reading;}catch(Exception)when(player.ReadingStop?.IsCancellationRequested==true){}
        player.Client?.Dispose();player.Socket?.Dispose();player.ReadingStop?.Dispose();player.Client=null;player.Socket=null;player.Reading=null;player.ReadingStop=null;
    }
    public static async Task Run(string origin,string output,bool prepareOnly)
    {
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Scheme!="https"&&!(prepareOnly&&uri.IsLoopback))throw new Exception("Dolphin qualification requires a public HTTPS origin");
        string cardOrigin=uri.Scheme=="https"?origin:"https://fixture-only.invalid";
        Directory.CreateDirectory(output);using var api=new HttpClient{BaseAddress=new Uri(origin),Timeout=TimeSpan.FromSeconds(20)};
        async Task<JsonElement> Request(HttpMethod method,string path,object? body=null,string? token=null)
        {
            using var request=new HttpRequestMessage(method,path);if(token!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            if(body!=null)request.Content=JsonContent.Create(body);using var response=await api.SendAsync(request);
            if(!response.IsSuccessStatusCode)throw new Exception("Dolphin fixture request failed: "+path+" HTTP "+(int)response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }
        async Task<Player> Create(string name,int seed,int species,bool upload)
        {
            var account=await Request(HttpMethod.Post,"/v1/auth/register",new{name=name+Guid.NewGuid().ToString("N")[..8],password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24))});
            Guid id=account.GetProperty("id").GetGuid();string browser=account.GetProperty("token").GetString()!;
            ulong town=BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));if(town==0)town=1;
            var state=new GameState(seed){Name=name,Species=species,Tutorial=7,Wallet=4356,Savings=9021};
            long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();state.ResidentJobs.Enable(town,now);
            state.Rollover((int)(now/86400),DateTime.UtcNow.Year*12+DateTime.UtcNow.Month-1);
            if(!upload&&!state.PlaceHouse(12,22,false,true))throw new Exception("Target fixture home plot blocked");
            for(int z=10;z<24;z++)for(int x=28;x<43;x++){state.Town.Tiles[z*Town.Width+x]=(byte)Terrain.Grass;state.Town.Elevation[z*Town.Width+x]=0;}
            for(int i=state.Town.GroundCount-1;i>=0;i--){var g=state.Town.Ground[i];if(g.X>=28&&g.X<43&&g.Z>=9&&g.Z<24)state.Town.RemoveGround(i);}
            state.Space=0;state.X=HelipadSite.CenterX+.5f;state.Z=HelipadSite.Z+HelipadSite.Depth+1.5f;state.Shirt=species==1?Catalog.BerryShirt:Catalog.StripedShirt;
            if(!upload&&(!HelipadSite.Exists(state.Town)||!HelipadSite.AtGate((int)state.X,(int)state.Z)||!BuildingWorld.CanStand(state,0,state.X,state.Z,0,true)))
                throw new Exception("Target console must start on walkable ground within the helipad interaction area");
            state.Pockets[2]=Catalog.WritingPaper;state.Pockets[3]=10;
            var disk=new Disk();var repository=new SaveRepository(disk);repository.Save(state);repository.Save(state);
            var start=await Request(HttpMethod.Post,"/v1/auth/link/start",new{town=town.ToString("X16")});
            await Request(HttpMethod.Post,"/v1/account/link",new{code=start.GetProperty("code").GetString()},browser);
            var linked=await Request(HttpMethod.Post,"/v1/auth/link/poll",new{secret=start.GetProperty("secret").GetString()});
            var player=new Player(id,browser,linked.GetProperty("deviceId").GetGuid(),town.ToString("X16"),disk)
            {Access=linked.GetProperty("token").GetString()!,Refresh=linked.GetProperty("refreshToken").GetString()!,RenewAt=DateTime.UtcNow.AddSeconds(540)};
            if(upload)
            {
                byte[] bytes=Envelope(disk);var offer=await Request(HttpMethod.Post,$"/v1/towns/{id}/checkpoint-uploads",new{length=bytes.Length,sha256=Convert.ToHexString(SHA256.HashData(bytes))},player.Access);
                for(int offset=0;offset<bytes.Length;offset+=16384)
                {
                    using var request=new HttpRequestMessage(HttpMethod.Put,$"/v1/towns/{id}/checkpoint-uploads/{offer.GetProperty("id").GetGuid()}/page?offset={offset}");
                    request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",player.Access);request.Content=new ByteArrayContent(bytes.AsSpan(offset,Math.Min(16384,bytes.Length-offset)).ToArray());request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");
                    using var response=await api.SendAsync(request);if(!response.IsSuccessStatusCode)throw new Exception("Target fixture card import failed at offset "+offset+" HTTP "+(int)response.StatusCode);
                }
            }
            else
            {
                new OnlineCredentialStore(disk).Save(null,new(cardOrigin,player.Town,id.ToString(),player.Device.ToString(),player.Refresh,1));
                var address=new byte[256];address[0]=1;address[1]=(byte)cardOrigin.Length;for(int i=0;i<cardOrigin.Length;i++)address[i+2]=(byte)cardOrigin[i];
                uint crc=SaveCodec.Crc(address,0,252);for(int i=0;i<4;i++)address[252+i]=(byte)(crc>>(i*8));disk.Files["AnimalTown-Service"]=address;
                string folder=Path.Combine(output,"card");Directory.CreateDirectory(folder);
                foreach(var file in disk.Files){string path=Path.Combine(folder,file.Key);File.WriteAllBytes(path,file.Value);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
            }
            return player;
        }
        var owner=await Create("Console",1838,0,false);var guests=new[]{await Create("GuestA",1860,1,true),await Create("GuestB",1861,2,true)};
        foreach(var guest in guests){await Request(HttpMethod.Post,$"/v1/friends/{guest.Id}",new{},owner.Browser);await Request(HttpMethod.Post,$"/v1/friends/{owner.Id}",new{},guest.Browser);}
        WriteJson(Path.Combine(output,"status.json"),new{ready=true,prepareOnly,physical_hardware=false});
        if(prepareOnly){Console.WriteLine("Private target fixture prepared through loopback; no Dolphin or public-route qualification.");return;}
        string database=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_CONNECTION")??throw new Exception("Target fixture requires its disposable database");
        await using var connection=new NpgsqlConnection(database);await connection.OpenAsync();
        bool joined=false,chatReceived=false,fenced=false,returned=false,moved=false;Guid fence=Guid.Empty,priorConnection=Guid.Empty;long sequenceBefore=0;float initialX=0,initialZ=0;DateTime deadline=DateTime.UtcNow.AddMinutes(25);
        async Task Connect(Player guest)
        {
            if(DateTime.UtcNow>=guest.RenewAt)
            {
                var renewal=await Request(HttpMethod.Post,"/v1/auth/device/renew",new{deviceId=guest.Device,refreshToken=guest.Refresh,rotationId=Guid.NewGuid()});
                guest.Access=renewal.GetProperty("token").GetString()!;guest.Refresh=renewal.GetProperty("refreshToken").GetString()!;guest.RenewAt=DateTime.UtcNow.AddSeconds(540);
            }
            if(guest.Visit==Guid.Empty)
            {
                guest.Visit=(await Request(HttpMethod.Post,"/v1/online/visits",new{host=owner.Id},guest.Access)).GetProperty("id").GetGuid();
                Guid reservation=Guid.NewGuid();string hash=Convert.ToHexString(SHA256.HashData(Envelope(guest.Card)));
                await Request(HttpMethod.Post,"/v1/online/reservations",new{id=reservation,host=owner.Id,visit=guest.Visit,revision=1,fingerprint=hash,profile=0},guest.Access);
                await Request(HttpMethod.Post,$"/v1/online/reservations/{reservation}/activate",new{},guest.Access);
            }
            var ticket=await Request(HttpMethod.Post,"/v1/gameplay/tickets",new{host=owner.Id,visit=guest.Visit},guest.Access);
            var socket=new ClientWebSocket();socket.Options.AddSubProtocol("animaltown.v1");
            try
            {
                using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(15));await socket.ConnectAsync(new UriBuilder(origin){Scheme="wss",Path="/v1/gameplay"}.Uri,stop.Token);
                var frame=new byte[44];OnlineWire.TryWriteHeader(frame,0,frame.Length,new(OnlineFrameType.GameplayTicket,32,0,0));Convert.FromHexString(ticket.GetProperty("ticket").GetString()!).CopyTo(frame,12);
                await socket.SendAsync(frame,WebSocketMessageType.Binary,true,stop.Token);
                var hello=new byte[64];int count=0;WebSocketReceiveResult part;do{part=await socket.ReceiveAsync(new ArraySegment<byte>(hello,count,hello.Length-count),stop.Token);count+=part.Count;}while(!part.EndOfMessage);
                if(OnlineWire.ReadFrame(hello,0,count,out var header,out _)!=OnlineFrameRead.Complete||header.Type!=OnlineFrameType.Pong)throw new Exception("Target companion handshake failed");
                guest.Socket=socket;guest.Client=new(socket,header.Epoch,0,(byte)header.Sequence);guest.Client.StartHeartbeat();
                if((await guest.Client.Command(new(){Kind=OnlineCommandKind.Snapshot})).Status!=200)throw new Exception("Target companion snapshot failed");
                while(guest.Client.State.NeedsSnapshot)await guest.Client.Receive();
                while(guest.Client.Position.X==0)await guest.Client.Receive();
                await guest.Client.WalkToPoint(HelipadSite.CenterX+.5f+(guest==guests[0]?-2:2),HelipadSite.Z+HelipadSite.Depth+3.5f);
                if((await guest.Client.Command(new(){Kind=OnlineCommandKind.Chat,Text="Hello from "+(guest==guests[0]?"GuestA":"GuestB")})).Status!=200)throw new Exception("Target companion chat failed");
                var receiving=guest.Client;var readingStop=guest.ReadingStop=new CancellationTokenSource();
                guest.Reading=Task.Run(async()=>
                {
                    try
                    {
                        while(!readingStop.IsCancellationRequested)
                        {
                            await receiving.Receive();
                            if(Enumerable.Range(0,receiving.State.ChatCount).Any(i=>receiving.State.ChatAt(i).Name=="Console"&&receiving.State.ChatAt(i).Text is "a" or "A"))guest.SawConsoleChat=true;
                        }
                    }catch(Exception)when(readingStop.IsCancellationRequested){}
                });
            }catch{socket.Dispose();throw;}
        }
        try
        {
            while(DateTime.UtcNow<deadline&&!File.Exists(Path.Combine(output,"stop")))
            {
                bool gate;long generation,revision;string fingerprint;int actors;float x=0,z=0;Guid current=Guid.Empty;
                await using(var query=new NpgsqlCommand("""
                    SELECT d.generation,COALESCE(h.revision,0),COALESCE(h.fingerprint,''),EXISTS(SELECT 1 FROM gates WHERE host=$1),
                      (SELECT count(*) FROM hosted_actors WHERE host=$1 AND expires>clock_timestamp())
                    FROM devices d LEFT JOIN online_card_heads h ON h.player=d.player WHERE d.id=$2
                    """,connection))
                {
                    query.Parameters.AddWithValue(owner.Id);query.Parameters.AddWithValue(owner.Device);await using var row=await query.ExecuteReaderAsync();if(!await row.ReadAsync())throw new Exception("Target device disappeared");
                    generation=row.GetInt64(0);revision=row.GetInt64(1);fingerprint=row.GetString(2);gate=row.GetBoolean(3);actors=(int)row.GetInt64(4);
                }
                await using(var query=new NpgsqlCommand("SELECT x,z,connection FROM hosted_actors WHERE host=$1 AND player=$1",connection))
                {query.Parameters.AddWithValue(owner.Id);await using var row=await query.ExecuteReaderAsync();if(await row.ReadAsync()){x=row.GetFloat(0);z=row.GetFloat(1);current=row.GetGuid(2);}}
                if(gate&&!joined){foreach(var guest in guests)await Connect(guest);joined=true;}
                if(joined)
                {
                    foreach(var guest in guests)
                    {if(guest.Reading?.IsFaulted==true)await guest.Reading;if(DateTime.UtcNow>=guest.RenewAt){await Stop(guest);await Connect(guest);}}
                    chatReceived|=guests.All(g=>g.SawConsoleChat);
                }
                if(joined&&actors==3&&sequenceBefore==0&&current!=Guid.Empty){sequenceBefore=revision;initialX=x;initialZ=z;}
                moved|=sequenceBefore>0&&(x-initialX)*(x-initialX)+(z-initialZ)*(z-initialZ)>.09f;
                if(File.Exists(Path.Combine(output,"disconnect"))&&!fenced&&chatReceived)
                {
                    // Fence only this fixture's current socket. The real client
                    // must obtain a fresh ticket and resume its reservation.
                    priorConnection=current;fence=Guid.NewGuid();await using var fault=new NpgsqlCommand("UPDATE hosted_actors SET connection=$2 WHERE host=$1 AND player=$1",connection);fault.Parameters.AddWithValue(owner.Id);fault.Parameters.AddWithValue(fence);await fault.ExecuteNonQueryAsync();fenced=true;
                }
                bool reconnected=fenced&&current!=Guid.Empty&&current!=fence&&current!=priorConnection;
                if(sequenceBefore>0&&revision>sequenceBefore)
                {
                    await using var pending=new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM online_travel WHERE player=$1 AND phase<3)",connection);pending.Parameters.AddWithValue(owner.Id);returned=await pending.ExecuteScalarAsync() is true;
                }
                WriteJson(Path.Combine(output,"status.json"),new{ready=true,public_https=true,physical_hardware=false,renewed=generation>1,gate,joined,actors,moved,chatReceived,fenced,reconnected,returned,revision,fingerprint,town=owner.Town});
                if(returned){Console.WriteLine("Real console device returned and acknowledged its published card; target report awaits raw-card verification.");return;}
                await Task.Delay(200);
            }
            if(!File.Exists(Path.Combine(output,"stop")))throw new Exception("Dolphin target fixture exceeded its bounded deadline");
        }
        finally{foreach(var guest in guests)await Stop(guest);}
    }
    public static void Verify(string directory,string statusPath)
    {
        var disk=new Disk();foreach(string path in Directory.GetFiles(directory))disk.Files[Path.GetFileName(path)]=File.ReadAllBytes(path);
        var state=new SaveRepository(disk).Load()??throw new Exception("Returned target card cannot load");
        var status=JsonDocument.Parse(File.ReadAllText(statusPath)).RootElement;
        string hash=Convert.ToHexString(SHA256.HashData(Envelope(disk)));var journal=new OnlineTravelStore(disk).Load();
        if(!status.GetProperty("returned").GetBoolean()||state.Name!="Console"||state.Wallet!=4356||state.Town.Jobs.TownId.ToString("X16")!=status.GetProperty("town").GetString()||
           hash!=status.GetProperty("fingerprint").GetString()||journal==null||journal.Record.Phase!=OnlineTravelPhase.Ready||journal.Record.Fingerprint!=hash)
            throw new Exception("Returned target card does not match its acknowledged private home and Ready recovery journal");
        WriteJson(Path.Combine(Path.GetDirectoryName(statusPath)!,"card-verification.json"),new{passed=true,home_identity=true,private_wallet=true,ready_journal=true,acknowledged_fingerprint=true,physical_hardware=false});
        Console.WriteLine("Dolphin return card reload, private economy, journal and server-acknowledged SHA-256 verified.");
    }
}
