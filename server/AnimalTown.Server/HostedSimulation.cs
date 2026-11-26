using AnimalTown;
using AnimalTown.Jobs;
using Npgsql;

// Scheduled processing continues while the owner's console is off. Every
// mutation still takes the same town/player locks as gameplay commands.
sealed class HostedSimulation(GameDatabase database,LiveTownTransport transport,ILogger<HostedSimulation> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var ticks=new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while(await ticks.WaitForNextTickAsync(stop))
            {
                try { await database.AdvanceHostedTowns(transport.WorkerId,stop);await database.DeliverHostedMail(transport.WorkerId,stop); }
                catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
                catch(Exception error){logger.LogDebug("Hosted scheduled work deferred after {FailureType}",error.GetType().Name);}
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
    }
}

sealed partial class GameDatabase
{
    internal async Task AdvanceHostedTowns(Guid worker,CancellationToken stop)
    {
        var opened=new List<(Guid Host,Guid Gate)>();
        await using(var c=await source.OpenConnectionAsync(stop))
        await using(var read=Cmd(c,null,"""
            SELECT g.host,g.generation FROM gates g JOIN initial_town_imports i ON i.player=g.host
            LEFT JOIN hosted_towns h ON h.host=g.host
            WHERE g.expires>clock_timestamp() AND (h.host IS NULL OR h.worker=$1 OR h.expires<=clock_timestamp())
            ORDER BY coalesce(h.simulated_at,0),g.host LIMIT 32
            """,worker))
        await using(var rows=await read.ExecuteReaderAsync(stop))while(await rows.ReadAsync(stop))opened.Add((rows.GetGuid(0),rows.GetGuid(1)));
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach(var entry in opened)
        {
            stop.ThrowIfCancellationRequested();
            uint epoch;
            try { epoch=await ClaimHostedTown(entry.Host,entry.Gate,worker); }
            catch(GameError error)when(error.Status==503){continue;}
            await using var c=await source.OpenConnectionAsync(stop);await using var t=await c.BeginTransactionAsync(stop);
            await LockTown(c,t,entry.Host);
            await RequireHostedWriter(c,t,new(entry.Host,entry.Host,Guid.Empty,Guid.Empty,worker,epoch,null!));
            long previous;
            await using(var read=Cmd(c,t,"SELECT simulated_at FROM hosted_towns WHERE host=$1",entry.Host))previous=(long)(await read.ExecuteScalarAsync(stop))!;
            if(previous>=now)continue;
            var loaded=await Load(c,t,entry.Host,false);var world=loaded.State;
            byte[] personalBefore=InventoryPacket(world,loaded.Revision);
            int day=(int)(now/86400);bool newDay=world.AdvanceHostedCalendar(day,DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
            world.ResidentJobs.EnableOnlineAdapter(now);
            if(!await ReviewActive(c,t,entry.Host))
            {
                world.Town.JobState.Advance(60);
                var journey=new VillagerJourney();
                await using(var talking=Cmd(c,t,"""
                    SELECT DISTINCT talk.npc FROM hosted_job_conversations talk JOIN hosted_actors actor
                    ON actor.host=talk.host AND actor.player=talk.player
                    WHERE talk.host=$1 AND talk.expires>clock_timestamp() AND actor.updated>clock_timestamp()-interval '3 seconds'
                    """,entry.Host))
                await using(var rows=await talking.ExecuteReaderAsync(stop))while(await rows.ReadAsync(stop))journey.ExternallyPaused[rows.GetInt32(0)]=true;
                // No owner-console ghost blocks unattended NPCs. Restore the
                // actual home-profile location before canonical publication.
                float oldX=world.X,oldZ=world.Z;int oldRoom=world.Space;
                try { world.Space=-1;for(int step=0;step<60;step++)journey.Tick(world); }
                finally { world.X=oldX;world.Z=oldZ;world.Space=oldRoom; }
                byte[] poses=Packet(w=>
                {
                    w.Int(Town.SavedVillagerCount);
                    for(int npc=0;npc<Town.SavedVillagerCount;npc++)
                    {var actor=world.Town.Residents[npc];w.Int(npc);w.Int(actor.Space);w.Int((int)(actor.PosX*65536));w.Int((int)(actor.PosZ*65536));w.Int((int)(actor.Yaw*65536));}
                });
                await AppendHostedEvent(c,t,entry.Host,null,8,null,poses);
            }
            // Expired offers/claims release their scarce shared slots. Bound
            // parcels remain escrowed until the owner next reconciles/returns.
            await using(var expire=Cmd(c,t,"UPDATE hosted_jobs SET phase=7,version=version+1 WHERE host=$1 AND phase<=4 AND due<$2 AND parcel_pocket<0",entry.Host,now))await expire.ExecuteNonQueryAsync(stop);
            byte[] personalAfter=InventoryPacket(world,loaded.Revision);
            bool personalChanged=!personalBefore.AsSpan().SequenceEqual(personalAfter);
            if(personalChanged)
            {
                await Save(c,t,entry.Host,world);
                await AppendHostedEvent(c,t,entry.Host,entry.Host,2,entry.Host,InventoryPacket(world,loaded.Revision+1));
            }
            else
            {
                // NPC/job clocks do not invalidate an unchanged pocket revision
                // every second while a player is choosing a placement or shop row.
                world.Sequence=checked(world.Sequence+1);await WriteParts(c,t,entry.Host,world,false);
            }
            if(newDay)await AppendHostedEvent(c,t,entry.Host,null,1,null,[1]);
            await using(var mark=Cmd(c,t,"UPDATE hosted_towns SET simulated_at=$2 WHERE host=$1",entry.Host,now))await mark.ExecuteNonQueryAsync(stop);
            await t.CommitAsync(stop);
        }
    }

    internal async Task DeliverHostedMail(Guid worker,CancellationToken stop)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pending=new List<Guid>(16);
        await using(var c=await source.OpenConnectionAsync(stop))
        await using(var read=Cmd(c,null,"SELECT id FROM hosted_mail WHERE NOT replied AND due<=$1 ORDER BY due,id LIMIT 16",now))
        await using(var rows=await read.ExecuteReaderAsync(stop))while(await rows.ReadAsync(stop))pending.Add(rows.GetGuid(0));
        foreach(var id in pending)
        {
            stop.ThrowIfCancellationRequested();
            await DeliverHostedLetter(id,now,worker,stop);
        }
    }

    async Task DeliverHostedLetter(Guid id,long now,Guid worker,CancellationToken stop)
    {
        await using var c=await source.OpenConnectionAsync(stop);await using var t=await c.BeginTransactionAsync(stop);
        Guid host,sender;
        await using(var read=Cmd(c,t,"SELECT host,sender FROM hosted_mail WHERE id=$1 AND NOT replied",id))
        await using(var row=await read.ExecuteReaderAsync(stop))
        {if(!await row.ReadAsync(stop))return;host=row.GetGuid(0);sender=row.GetGuid(1);}
        if(host==sender)await LockTown(c,t,host);else await LockVisitParticipants(c,t,host,sender);
        if(!await ClaimHostedBackgroundWriter(c,t,host,worker))return;
        string body,senderKey,recipientKey;int npc,profile;long due;bool delivered;
        await using(var read=Cmd(c,t,"SELECT body,sender_identity,recipient_identity,recipient_npc,recipient_profile,due,delivered FROM hosted_mail WHERE id=$1 AND NOT replied FOR UPDATE",id))
        await using(var row=await read.ExecuteReaderAsync(stop))
        {
            if(!await row.ReadAsync(stop))return;
            body=row.GetString(0);senderKey=row.GetString(1);recipientKey=row.GetString(2);npc=row.GetInt32(3);profile=row.GetInt32(4);due=row.GetInt64(5);delivered=row.GetBoolean(6);
        }
        if(due>now)return;
        var town=(await Load(c,t,host,false)).State;
        var home=host==sender?town:(await Load(c,t,sender,false)).State;
        var recipient=ParseJobKey(recipientKey);var author=ParseJobKey(senderKey);
        town.ResidentJobs.EnableOnlineAdapter(now);home.ResidentJobs.EnableOnlineAdapter(now);
        bool townChanged=false,homeChanged=false,replied=false;
        if(npc<0)
        {
            if((uint)profile>=GameState.ProfileCount||town.Town.Jobs.Player(profile)!=recipient)return;
            if(!town.ResidentJobs.DeliverOnlineText(author,recipient,body,due,await NextHostedIdentity(c,t)))return;
            delivered=replied=townChanged=true;
        }
        else
        {
            if((uint)npc>=Town.SavedVillagerCount||town.Town.Jobs.Npc(npc)!=recipient||!JobLetterGrader.TryGrade(body,false,out var grade))return;
            int owner=home.Town.Jobs.FindPlayer(author);if((uint)owner>=GameState.ProfileCount||!home.Profiles[owner].Created)return;
            int active=home.ActiveProfile;if(active!=owner)home.SelectProfile(owner);
            if(!delivered)
            {
                home.ResidentJobs.RecordOnlineFriendship(recipient,town.Town.Jobs.TownId,town.Town.Residents[npc].Name,grade.Rank>=3?3:-2);
                var favors=(await ReadHostedJobs(c,t,host,sender)).Where(j=>j.Villager==senderKey&&j.Template==JobTemplate.FavorLetter&&j.Requester==npc&&j.Phase==1&&due>=j.Accepted&&due<=j.Due).ToList();
                if(favors.Count>0)
                    await AwardHostedJob(c,t,host,sender,town,home,favors[0],npc,0,due,grade.Rank);
                delivered=true;homeChanged=true;townChanged=favors.Count>0;
            }
            if(now>=due+86400)
            {
                replied=home.ResidentJobs.DeliverOnlineText(recipient,author,VillagerJobService.OnlineLetterReply(grade.Rank),due+86400,await NextHostedIdentity(c,t));
                homeChanged|=replied;
            }
            if(active!=owner){home.CaptureProfile();home.SelectProfile(active);}
        }
        if(townChanged)await Save(c,t,host,town);
        if(homeChanged&&(sender!=host||!townChanged))await Save(c,t,sender,home);
        if(homeChanged)
        {
            var updated=await Load(c,t,sender,false);
            await AppendHostedEvent(c,t,host,sender,2,sender,InventoryPacket(updated.State,updated.Revision));
        }
        await using(var mark=Cmd(c,t,"UPDATE hosted_mail SET delivered=$2,replied=$3 WHERE id=$1",id,delivered,replied))await mark.ExecuteNonQueryAsync(stop);
        await t.CommitAsync(stop);
    }

    static async Task<bool> ClaimHostedBackgroundWriter(NpgsqlConnection c,NpgsqlTransaction t,Guid host,Guid worker)
    {
        bool reuse=false;
        await using(var read=Cmd(c,t,"SELECT worker,expires>clock_timestamp() FROM hosted_towns WHERE host=$1",host))
        await using(var row=await read.ExecuteReaderAsync())
        {
            if(!await row.ReadAsync())return false;
            if(row.GetBoolean(1)&&row.GetGuid(0)!=worker)return false;
            reuse=row.GetBoolean(1)&&row.GetGuid(0)==worker;
        }
        // Mail remains deliverable after the gate closes. It uses the same
        // worker lease and host row lock, without reopening visitor admission.
        await using var claim=Cmd(c,t,"UPDATE hosted_towns SET worker=$2,expires=clock_timestamp()+interval '10 seconds'"+
            (reuse?"":",epoch=nextval('hosted_epoch')")+" WHERE host=$1",host,worker);
        return await claim.ExecuteNonQueryAsync()==1;
    }
}
