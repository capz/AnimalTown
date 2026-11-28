using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;
using Npgsql;

static partial class HostedVisitChecks
{
    readonly record struct JobView(OnlineId Id,int Requester,int Target,JobTemplate Template,int Item,int Phase,int Version,long Due,int Pocket)
    {
        internal static JobView Read(Reply reply)
        {
            if(reply.Status!=200)throw new Exception("Job command: "+reply.Status+" "+reply.Error);
            var r=new OnlinePacketReader(reply.Extra,0,reply.Extra.Length);
            return new(r.Id(),r.Int(),r.Int(),(JobTemplate)r.Int(),r.Int(),r.Int(),r.Int(),r.Long(),r.Int());
        }
    }
    static Guid FixtureGuid(OnlineId id)=>Guid.Parse(OnlineIdentifiers.Format(id));
    static async Task JobFamilies(Client host,Client guest,Client other,Action<bool,string> check)
    {
        string? database=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_CONNECTION");
        if(string.IsNullOrEmpty(database))return;
        await using var connection=new NpgsqlConnection(database);await connection.OpenAsync();
        Guid hostId=FixtureGuid(host.State.Humans[0].Id);
        // Freeze offer selection only. Acceptance, physical conversations,
        // parcel escrow, handoffs and rewards use the actual public WSS path.
        async Task<OnlineId> Seed(Client actor,JobTemplate template,int item=0,int target=2,long due=0)
        {
            Guid player=FixtureGuid(actor.State.Humans[actor.Position.Slot].Id),id=Guid.NewGuid();
            string villager;int profile;
            await using(var read=new NpgsqlCommand("""
                SELECT p.town_data,p.house_positions,p.shop_states,p.player_villager,p.player_villager2,p.player_villager3,p.player_villager4,t.profile
                FROM players p JOIN online_travel t ON t.player=p.id AND t.phase=1 WHERE p.id=$1
                """,connection))
            {
                read.Parameters.AddWithValue(player);await using var row=await read.ExecuteReaderAsync();
                if(!await row.ReadAsync())throw new Exception("Job fixture requires its own active card reservation");
                var parts=new byte[7][];for(int i=0;i<parts.Length;i++)parts[i]=row.GetFieldValue<byte[]>(i);
                var state=SplitSaveCodec.Decode(parts);profile=row.GetInt32(7);var identity=state.Town.Jobs.Player(profile);
                villager=identity.Town.ToString("X16")+"/"+identity.Serial.ToString("X16");
            }
            long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await using var insert=new NpgsqlCommand("""
                INSERT INTO hosted_jobs(id,host,player,villager,profile,requester,target,template,item,phase,accepted,due,borrowers)
                VALUES($1,$2,$3,$4,$5,1,$6,$7,$8,0,$9,$10,$11)
                """,connection);
            foreach(object value in new object[]{id,hostId,player,villager,profile,target,(int)template,item,now,due==0?now+86400:due,template==JobTemplate.RetrievalBorrowed?1:0})insert.Parameters.AddWithValue(value);
            await insert.ExecuteNonQueryAsync();return OnlineIdentifiers.Parse(id.ToString());
        }
        async Task<Reply> Converse(Client actor,int npc)
        {
            Reply result=default;
            for(int attempt=0;attempt<4;attempt++)
            {
                await actor.WalkToResident(npc);var identity=actor.State.World!.Town.Jobs.Npc(npc);
                result=await actor.Command(new(){Kind=OnlineCommandKind.AskJob,A=npc,Entity=new(identity.Town,identity.Serial)});
                if(result.Status!=409||result.Error!="resident-out-of-reach")return result;
            }
            return result;
        }
        if(guest.Position.Room==TownShop.Room)check((await guest.Command(new(){Kind=OnlineCommandKind.EnterRoom,A=7})).Status==200,"Job participant leaves the shop normally");
        await guest.Snapshot();
        foreach(var template in new[]{JobTemplate.DeliveryClothing,JobTemplate.DeliveryProperty,JobTemplate.RetrievalBorrowed})
        {
            int item=template==JobTemplate.DeliveryClothing?Catalog.WorkShirt:Catalog.Notebook;
            var id=await Seed(guest,template,item);var offered=JobView.Read(await Converse(guest,1));
            check(offered.Id.Same(id)&&offered.Template==template,"Physical conversation exposes the selected eligible "+template);
            var accept=new OnlineCommand{Kind=OnlineCommandKind.AcceptJob,Entity=id,Revision=offered.Version};
            var current=JobView.Read(await guest.Command(accept));
            check((await guest.Command(accept)).Status==200,"Repeated "+template+" acceptance retains one parcel/context");
            await guest.Snapshot();
            if(current.Pocket>=0)
            {
                check(guest.State.Pockets[current.Pocket]==JobContent.Parcel,"Accepted delivery contains its bound parcel");
                var stolen=await guest.Command(new(){Kind=OnlineCommandKind.Place,Revision=guest.State.InventoryRevision,B=current.Pocket,C=1,D=1});
                check(stolen.Status!=200,"A job parcel cannot become guest furnishing");
            }
            for(int stage=0;stage<4&&current.Phase<4;stage++)
            {
                int npc=current.Template==JobTemplate.RetrievalBorrowed&&current.Phase==2?current.Requester:current.Target;
                await Converse(guest,npc);
                var handoff=new OnlineCommand{Kind=OnlineCommandKind.CompleteJob,Entity=id,Revision=current.Version,A=npc};
                var completed=await guest.Command(handoff);current=JobView.Read(completed);
                check((await guest.Command(handoff)).Status==200,"Repeated "+template+" handoff returns the durable receipt");
            }
            check(current.Phase>=4,"Supported "+template+" completes through authoritative physical interactions");
            if(current.Phase==5)check((await guest.Command(new(){Kind=OnlineCommandKind.CollectReward,Entity=id,Revision=current.Version})).Status==200,"Deferred delivery/retrieval reward remains collectible");
            await guest.Snapshot();check(!guest.State.Pockets.Contains(JobContent.Parcel),"Completion consumes the exact parcel and retains earned value");
        }
        await guest.Snapshot();await other.Snapshot();
        int fruit=guest.State.Pockets.First(i=>Catalog.Has(i,ItemFlags.Fruit)&&i!=guest.State.World!.Town.Jobs.NativeFruit);
        var first=await Seed(guest,JobTemplate.FavorFruit,fruit,-1);var second=await Seed(other,JobTemplate.FavorFruit,fruit,-1);
        var firstOffer=JobView.Read(await Converse(guest,1));var secondOffer=JobView.Read(await Converse(other,1));
        var one=new OnlineCommand{Kind=OnlineCommandKind.AcceptJob,Entity=first,Revision=firstOffer.Version};
        var two=new OnlineCommand{Kind=OnlineCommandKind.AcceptJob,Entity=second,Revision=secondOffer.Version};
        var outcomes=await Task.WhenAll(guest.Command(one),other.Command(two));
        check(outcomes.Count(r=>r.Status==200)==1&&outcomes.Count(r=>r.Status==409)==1,"Two humans racing the same shared favor produce exactly one claim");
        Client winner=outcomes[0].Status==200?guest:other,loser=ReferenceEquals(winner,guest)?other:guest;
        JobView favor=JobView.Read(outcomes[0].Status==200?outcomes[0]:outcomes[1]);
        await winner.Snapshot();int pocket=Array.IndexOf(winner.State.Pockets,fruit);
        var finish=new OnlineCommand{Kind=OnlineCommandKind.CompleteJob,Entity=favor.Id,Revision=favor.Version,A=1,B=pocket};
        check((await winner.Command(finish)).Status==200&&(await winner.Command(finish)).Status==200,"A shared fruit favor consumes supply and awards earnings once");
        var unaccepted=ReferenceEquals(loser,guest)?one:two;
        check((await loser.Command(new(){Kind=OnlineCommandKind.AbandonJob,Entity=unaccepted.Entity,Revision=unaccepted.Revision})).Status==200,"An unclaimed competing offer can be dismissed");
        var unsupported=await Seed(loser,JobTemplate.FavorBall,target:-1);
        var visible=await loser.Command(new(){Kind=OnlineCommandKind.Jobs});
        var listing=new OnlinePacketReader(visible.Extra,0,visible.Extra.Length);int count=listing.Int();bool exposed=false;
        for(int i=0;i<count;i++)
        {var id=listing.Id();listing.Int();listing.Int();listing.Int();listing.Int();listing.Int();listing.Int();listing.Long();listing.Int();exposed|=id.Same(unsupported);}
        check(!exposed,"Requests requiring excluded actions stay hidden from the online jobs list");
        var rejected=await loser.Command(new(){Kind=OnlineCommandKind.AcceptJob,Entity=unsupported,Revision=1});
        check(rejected.Status==403&&rejected.Error=="unsupported-online-job","A forged excluded-action job cannot enable tool gameplay");
        await using(var remove=new NpgsqlCommand("DELETE FROM hosted_jobs WHERE id=$1 AND phase=0",connection)){remove.Parameters.AddWithValue(FixtureGuid(unsupported));await remove.ExecuteNonQueryAsync();}
        var expired=await Seed(loser,JobTemplate.DeliveryProperty,10,due:DateTimeOffset.UtcNow.ToUnixTimeSeconds()-1000);
        rejected=await loser.Command(new(){Kind=OnlineCommandKind.AcceptJob,Entity=expired,Revision=1});
        check(rejected.Status is 404 or 409,"An expired offer cannot grant a parcel or reward");
    }
}
