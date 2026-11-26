using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;
using Npgsql;

static class ResidentReviewChecks
{
    sealed class Card:ISaveStorage
    {
        readonly Dictionary<string,byte[]> records=new();
        public byte[] Read(string name)=>records.TryGetValue(name,out var bytes)?(byte[])bytes.Clone():throw new FileNotFoundException();
        public void Write(string name,byte[] bytes)=>records[name]=(byte[])bytes.Clone();
    }
    static byte[] Envelope(GameState state)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        var parts=SplitSaveCodec.Encode(state);var commit=SplitSaveCodec.EncodeCommit(state,parts);
        writer.Write(commit.Length);writer.Write(commit);foreach(var p in parts){writer.Write(p.Length);writer.Write(p);}return stream.ToArray();
    }
    static string Key(JobIdentity id)=>id.Town.ToString("X16")+"/"+id.Serial.ToString("X16");
    public static async Task Run(string origin,Action<bool,string> check)
    {
        using var api=new HttpClient{BaseAddress=new Uri(origin),Timeout=TimeSpan.FromSeconds(20)};
        string token="";
        async Task<JsonElement> Request(HttpMethod method,string path,object? body=null,string? credential=null,int expected=200)
        {
            using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",credential??token);
            if(body!=null)request.Content=JsonContent.Create(body);
            using var response=await api.SendAsync(request);string content=await response.Content.ReadAsStringAsync();
            check((int)response.StatusCode==expected,$"Resident review {method} {path}: expected {expected}, received {(int)response.StatusCode}: {content}");
            return JsonDocument.Parse(content).RootElement.Clone();
        }
        string suffix=Guid.NewGuid().ToString("N")[..8];
        // Registration and linking are real device APIs. SQL below supplies only
        // deterministic earned effects for this newly created fixture account.
        var account=await Request(HttpMethod.Post,"/v1/auth/register",new{name="Roster"+suffix,password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24))},expected:200);
        Guid owner=account.GetProperty("id").GetGuid();string browser=account.GetProperty("token").GetString()!;
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();ulong town=BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));if(town==0)town=1;
        var baseline=new GameState(10091){Name="Fern",Sequence=11,Savings=5000,Tutorial=7};baseline.ResidentJobs.Enable(town,now);
        baseline.SelectProfile(1);baseline.Name="Mira";baseline.Savings=100;baseline.Tutorial=7;baseline.ResidentJobs.Enable(town,now);baseline.SelectProfile(0);baseline.CaptureProfile();
        baseline.Rollover((int)(now/86400),DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
        var link=await Request(HttpMethod.Post,"/v1/auth/link/start",new{town=town.ToString("X16")});
        await Request(HttpMethod.Post,"/v1/account/link",new{code=link.GetProperty("code").GetString()},browser);
        token=(await Request(HttpMethod.Post,"/v1/auth/link/poll",new{secret=link.GetProperty("secret").GetString()})).GetProperty("token").GetString()!;
        async Task<JsonElement> Upload(GameState state,long? parent)
        {
            byte[] bytes=Envelope(state);string hash=Convert.ToHexString(SHA256.HashData(bytes));
            var offer=await Request(HttpMethod.Post,$"/v1/towns/{owner}/checkpoint-uploads",new{length=bytes.Length,sha256=hash,parentRevision=parent});
            JsonElement result=offer;
            for(int offset=0;offset<bytes.Length;offset+=16384)
            {
                using var request=new HttpRequestMessage(HttpMethod.Put,$"/v1/towns/{owner}/checkpoint-uploads/{offer.GetProperty("id").GetGuid()}/page?offset={offset}");
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Content=new ByteArrayContent(bytes.AsSpan(offset,Math.Min(16384,bytes.Length-offset)).ToArray());request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");
                using var response=await api.SendAsync(request);string content=await response.Content.ReadAsStringAsync();
                check(response.IsSuccessStatusCode,"Resident checkpoint page: "+content);result=JsonDocument.Parse(content).RootElement.Clone();
            }
            return result;
        }
        await Upload(baseline,null);
        var offline=SplitSaveCodec.Decode(SplitSaveCodec.Encode(baseline));offline.HostedCommerceLocked=true;
        offline.SelectProfile(1);offline.Savings+=5000;offline.DeleteProfile(0);
        offline.SelectProfile(0);offline.Name="Reed";offline.Savings=25;offline.Tutorial=7;offline.ResidentJobs.Enable(town,now);
        offline.SelectProfile(2);offline.Name="Lark";offline.Wallet=432;offline.Tutorial=7;offline.ResidentJobs.Enable(town,now);
        var firstCard=new Card();var secondCard=new Card();var first=new SaveRepository(firstCard);var second=new SaveRepository(secondCard);first.Save(offline);
        var destination=new GameState(10092){Tutorial=7};destination.ResidentJobs.Enable(town^0x1000000000000000UL,now);second.Save(destination);
        var departed=baseline.Town.Jobs.Npc(1);offline=new OfflineTownTravel(first,second).ExchangeResidents(1,4,now);
        check(offline.Town.Jobs.Npc(1)!=departed&&second.Load()!.Town.Jobs.Npc(4)==departed,"Fixture completed a real two-card resident exchange before online review");
        JobIdentity reward=default;
        string? database=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_CONNECTION");
        if(!string.IsNullOrEmpty(database))
        {
            await using var connection=new NpgsqlConnection(database);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();
            var hosted=SplitSaveCodec.Decode(SplitSaveCodec.Encode(baseline));hosted.Savings+=1240;hosted.ResidentJobs.EnableOnlineAdapter(now);
            async Task<JobIdentity> Identity()
            {
                await using var read=new NpgsqlCommand("SELECT issuer,nextval('hosted_identity_counter') FROM hosted_identity WHERE id=1",connection,transaction);
                await using var row=await read.ExecuteReaderAsync();if(!await row.ReadAsync())throw new Exception("Missing hosted issuer");return new(Convert.ToUInt64(row.GetString(0),16),(ulong)row.GetInt64(1));
            }
            reward=hosted.ResidentJobs.RecordOnlineReward(departed,town,baseline.Town.Residents[1].Name,11,0,now,await Identity(),await Identity());
            check(hosted.ResidentJobs.DeliverOnlineText(departed,baseline.Town.Jobs.Player(0),"Your online progress has been kept safely.",now,await Identity()),"Fixture delivered new independently identified hosted mail");
            var p=SplitSaveCodec.Encode(hosted);
            await using var write=new NpgsqlCommand("UPDATE players SET town_data=$2,house_positions=$3,shop_states=$4,player_villager=$5,player_villager2=$6,player_villager3=$7,player_villager4=$8,revision=revision+1 WHERE id=$1",connection,transaction);
            write.Parameters.AddWithValue(owner);foreach(var part in p)write.Parameters.AddWithValue(part);check(await write.ExecuteNonQueryAsync()==1,"Earned fixture effects touch only its new account");await transaction.CommitAsync();
        }
        var uploaded=await Upload(offline,1);check(uploaded.GetProperty("revision").GetInt64()==1,"Uploading roster changes stages review without acknowledging a different card head");
        var head=await Request(HttpMethod.Get,"/v1/online/card");string offlineHash=Convert.ToHexString(SHA256.HashData(Envelope(offline)));
        check(head.GetProperty("residentReview").GetProperty("sha256").GetString()==offlineHash&&head.GetProperty("revision").GetInt64()==1,"Pending review is durably tied to the exact offline card and parent");
        var unchanged=await Request(HttpMethod.Get,"/v1/me");check(unchanged.GetProperty("name").GetString()=="Fern","Staging cannot replace the hosted roster before review");
        string lease=(await Request(HttpMethod.Post,"/v1/review",new{})).GetProperty("lease").GetGuid().ToString();
        string path="/v1/review/"+lease;
        var review=await Request(HttpMethod.Get,path+"/residents");
        check(review.GetProperty("entries").GetArrayLength()==3,"Review includes a reused player slot, an added profile and a completed NPC move");
        var scene=await Request(HttpMethod.Post,path+"/scene",new{});using var sceneBytes=new MemoryStream();int length=scene.GetProperty("length").GetInt32();
        while(sceneBytes.Length<length)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,path+"/scene?sha256="+scene.GetProperty("sha256").GetString()+"&offset="+sceneBytes.Length+"&count=16384");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            using var response=await api.SendAsync(request);response.EnsureSuccessStatusCode();sceneBytes.Write(await response.Content.ReadAsByteArrayAsync());
        }
        byte[] worldBytes=sceneBytes.ToArray();var world=OnlineTownProjection.Read(worldBytes,worldBytes.Length);
        check(Convert.ToHexString(SHA256.HashData(worldBytes))==scene.GetProperty("sha256").GetString()&&world.Profiles[0].Name=="Reed"&&world.Profiles[2].Name=="Lark"&&world.Town.Jobs.Npc(1)==offline.Town.Jobs.Npc(1),"Review scene shows the current uploaded town and completed move, with immutable verified public pages");
        check(world.Wallet==0&&world.Savings==0&&world.MailCount==0,"Review world transport excludes private pockets, money and mail");
        await Request(HttpMethod.Get,path+"/scene?sha256="+new string('0',64)+"&offset=0&count=16384",expected:409);
        var entry=review.GetProperty("entries")[0];Guid operation=Guid.NewGuid();string recipient=Key(offline.Town.Jobs.Player(1));
        var choice=new{id=operation,review=review.GetProperty("id").GetGuid(),version=review.GetProperty("version").GetInt32(),index=0,recipient};
        var chosen=await Request(HttpMethod.Post,path+"/residents/ops",choice);var retried=await Request(HttpMethod.Post,path+"/residents/ops",choice);
        check(chosen.GetProperty("version").GetInt32()==retried.GetProperty("version").GetInt32(),"Resident choice retries return the same durable receipt");
        await Request(HttpMethod.Post,path+"/residents/ops",new{id=Guid.NewGuid(),choice.review,choice.version,index=2,recipient=""},expected:409);
        await Request(HttpMethod.Delete,path);
        await BackendRestartChecks.Restart(check);
        lease=(await Request(HttpMethod.Post,"/v1/review",new{})).GetProperty("lease").GetGuid().ToString();path="/v1/review/"+lease;
        review=await Request(HttpMethod.Get,path+"/residents");check(review.GetProperty("entries")[0].GetProperty("approved").GetBoolean(),"An interrupted review retains the selected owner after a backend process restart");
        foreach(int index in new[]{2,5})
        {
            await Request(HttpMethod.Post,path+"/residents/ops",new{id=Guid.NewGuid(),review=review.GetProperty("id").GetGuid(),version=review.GetProperty("version").GetInt32(),index,recipient=""});review=await Request(HttpMethod.Get,path+"/residents");
        }
        var complete=new{id=Guid.NewGuid(),review=review.GetProperty("id").GetGuid(),version=review.GetProperty("version").GetInt32()};
        var applied=await Request(HttpMethod.Post,path+"/residents/complete",complete);var again=await Request(HttpMethod.Post,path+"/residents/complete",complete);
        check(applied.GetProperty("revision").GetInt64()==2&&again.GetProperty("revision").GetInt64()==2,"Review completion atomically advances once and survives response-loss retry after its lease ended");
        head=await Request(HttpMethod.Get,"/v1/online/card");check(head.GetProperty("residentReview").ValueKind==JsonValueKind.Null&&head.GetProperty("sha256").GetString()==offlineHash,"Reviewed card lineage remains the exact uploaded bytes until merged publication/readback");
        Guid reservation=Guid.NewGuid();await Request(HttpMethod.Post,"/v1/online/reservations",new{id=reservation,host=owner,visit=Guid.Empty,revision=2,fingerprint=offlineHash,profile=offline.ActiveProfile});await Request(HttpMethod.Post,$"/v1/online/reservations/{reservation}/activate",new{});
        var publication=await Request(HttpMethod.Post,$"/v1/online/reservations/{reservation}/settlement",new{});using var returned=new MemoryStream();
        while(returned.Length<publication.GetProperty("length").GetInt32())
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,$"/v1/online/reservations/{reservation}/checkpoint?offset={returned.Length}&count=16384");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            using var response=await api.SendAsync(request);response.EnsureSuccessStatusCode();returned.Write(await response.Content.ReadAsByteArrayAsync());
        }
        using var reader=new BinaryReader(new MemoryStream(returned.ToArray()));byte[] commit=reader.ReadBytes(reader.ReadInt32());var records=new byte[7][];for(int i=0;i<7;i++)records[i]=reader.ReadBytes(reader.ReadInt32());
        var saved=SplitSaveCodec.DecodeCommitted(commit,records);
        check(saved.Profiles[0].Name=="Reed"&&saved.Profiles[0].Savings==25&&saved.Profiles[2].Name=="Lark"&&saved.Town.Jobs.Npc(1)==offline.Town.Jobs.Npc(1)&&second.Load()!.Town.Jobs.Npc(4)==departed,"Merged publication preserves both sides of the completed move and the new profiles");
        if(!reward.IsEmpty)check(saved.Profiles[1].Savings==6340&&saved.Town.Jobs.Reward(saved.Town.Jobs.FindReward(reward)).Owner==saved.Town.Jobs.Player(1)&&saved.Town.Jobs.Letter(1,0).Recipient==saved.Town.Jobs.Player(1),"Only new hosted proceeds, mail and earned gifts transfer to the explicitly selected survivor");
        await Request(HttpMethod.Post,$"/v1/online/reservations/{reservation}/published",new{fingerprint=publication.GetProperty("sha256").GetString()});
        await Request(HttpMethod.Post,"/v1/online/closure",new{id=Guid.NewGuid(),revision=3,fingerprint=publication.GetProperty("sha256").GetString()},expected:409);
        // Reach the last-player state through the same uploaded conflict review,
        // rather than editing the server's roster or its acknowledged card head.
        saved.DeleteProfile(0);saved.DeleteProfile(2);saved.Sequence++;
        await Upload(saved,3);
        lease=(await Request(HttpMethod.Post,"/v1/review",new{})).GetProperty("lease").GetGuid().ToString();path="/v1/review/"+lease;
        review=await Request(HttpMethod.Get,path+"/residents");
        foreach(int index in new[]{0,2})
        {
            await Request(HttpMethod.Post,path+"/residents/ops",new{id=Guid.NewGuid(),review=review.GetProperty("id").GetGuid(),version=review.GetProperty("version").GetInt32(),index,recipient=Key(saved.Town.Jobs.Player(1))});
            review=await Request(HttpMethod.Get,path+"/residents");
        }
        await Request(HttpMethod.Post,path+"/residents/complete",new{id=Guid.NewGuid(),review=review.GetProperty("id").GetGuid(),version=review.GetProperty("version").GetInt32()});
        head=await Request(HttpMethod.Get,"/v1/online/card");reservation=Guid.NewGuid();
        await Request(HttpMethod.Post,"/v1/online/reservations",new{id=reservation,host=owner,visit=Guid.Empty,revision=4,fingerprint=head.GetProperty("sha256").GetString(),profile=1});
        await Request(HttpMethod.Post,$"/v1/online/reservations/{reservation}/activate",new{});
        await Request(HttpMethod.Post,"/v1/online/closure",new{id=Guid.NewGuid(),revision=4,fingerprint=head.GetProperty("sha256").GetString()},expected:409);
        publication=await Request(HttpMethod.Post,$"/v1/online/reservations/{reservation}/settlement",new{});
        await Request(HttpMethod.Post,$"/v1/online/reservations/{reservation}/published",new{fingerprint=publication.GetProperty("sha256").GetString()});
        await Request(HttpMethod.Post,"/v1/online/gate/open",new{});
        var closure=new{id=Guid.NewGuid(),revision=5,fingerprint=publication.GetProperty("sha256").GetString()};
        await Request(HttpMethod.Post,"/v1/online/closure",closure,expected:409);
        await Request(HttpMethod.Delete,"/v1/online/gate");
        var closed=await Request(HttpMethod.Post,"/v1/online/closure",closure);var closedAgain=await Request(HttpMethod.Post,"/v1/online/closure",closure);
        check(closed.GetProperty("closed").GetBoolean()&&closedAgain.GetProperty("revision").GetInt64()==5,"Last-resident closure waits for return acknowledgment and gate closure, then records one durable result");
        await Request(HttpMethod.Post,"/v1/online/gate/open",new{},expected:409);
        await Request(HttpMethod.Post,"/v1/online/reservations",new{id=Guid.NewGuid(),host=owner,visit=Guid.Empty,revision=5,fingerprint=closure.fingerprint,profile=1},expected:409);
        check((await Request(HttpMethod.Get,"/v1/online/card")).GetProperty("closed").GetBoolean(),"Closure prevents new admission or reservations until a new resident roster is reviewed");
    }
}
