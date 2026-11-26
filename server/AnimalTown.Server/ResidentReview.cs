using System.Text.Json;
using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;
using Npgsql;

sealed record ResidentReviewOperation(Guid Id,Guid Review,int Version,int Index,string Recipient);
sealed record ResidentReviewCompletion(Guid Id,Guid Review,int Version);

sealed partial class GameDatabase
{
    sealed record ResidentReview(Guid Id,long Parent,string Fingerprint,byte[] Commit,byte[][] Records,int Approved,string[] Recipients,int Version)
    {
        public GameState State()=>SplitSaveCodec.DecodeCommitted(Commit,Records);
    }

    static async Task StageResidentReview(NpgsqlConnection c,NpgsqlTransaction t,Guid owner,long parent,ParsedInitialTown upload)
    {
        await using var stage=Cmd(c,t,"""
            INSERT INTO offline_resident_reviews(player,id,parent_revision,fingerprint,save_commit,save_records)
            VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(player) DO UPDATE SET
              id=excluded.id,parent_revision=excluded.parent_revision,fingerprint=excluded.fingerprint,
              save_commit=excluded.save_commit,save_records=excluded.save_records,approved=0,
              recipients=ARRAY['','','',''],version=1
            WHERE offline_resident_reviews.fingerprint<>excluded.fingerprint OR offline_resident_reviews.parent_revision<>excluded.parent_revision
            """,owner,Guid.NewGuid(),parent,upload.Fingerprint,upload.Commit,upload.Records);
        await stage.ExecuteNonQueryAsync();
    }

    static async Task<ResidentReview> ReadResidentReview(NpgsqlConnection c,NpgsqlTransaction t,Guid owner)
    {
        await using var read=Cmd(c,t,"SELECT id,parent_revision,fingerprint,save_commit,save_records,approved,recipients,version FROM offline_resident_reviews WHERE player=$1",owner);
        await using var row=await read.ExecuteReaderAsync();
        if(!await row.ReadAsync())throw new GameError(409,"resident-review-ended");
        return new(row.GetGuid(0),row.GetInt64(1),row.GetString(2),row.GetFieldValue<byte[]>(3),row.GetFieldValue<byte[][]>(4),row.GetInt32(5),row.GetFieldValue<string[]>(6),row.GetInt32(7));
    }

    static async Task<GameState> ResidentBaseline(NpgsqlConnection c,NpgsqlTransaction t,Guid owner,ResidentReview review)
    {
        await using var read=Cmd(c,t,"SELECT save_commit,save_records FROM online_card_heads WHERE player=$1 AND revision=$2",owner,review.Parent);
        await using var row=await read.ExecuteReaderAsync();
        if(!await row.ReadAsync())throw new GameError(409,"card-lineage-changed");
        return SplitSaveCodec.DecodeCommitted(row.GetFieldValue<byte[]>(0),row.GetFieldValue<byte[][]>(1));
    }

    public async Task<object> ListResidentReview(DevicePrincipal device,Guid lease)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await LockTown(c,t,device.Player);await RequireActiveDevice(c,t,device);
        var hosted=(await Load(c,t,device.Player,false)).State;
        await RequirePlacementLease(c,t,device,lease,ReviewDeviceVillager(hosted,device));
        var review=await ReadResidentReview(c,t,device.Player);var local=review.State();var baseline=await ResidentBaseline(c,t,device.Player,review);
        int changed=OnlineResidentDecisions.Changes(local,baseline);var entries=new List<object>();
        for(int index=0;index<10;index++)
        {
            if((changed&(1<<index))==0)continue;
            bool profile=index<GameState.ProfileCount;int slot=profile?index:index-GameState.ProfileCount;
            var before=profile?baseline.Town.Jobs.Player(slot):baseline.Town.Jobs.Npc(slot);
            var after=profile?local.Town.Jobs.Player(slot):local.Town.Jobs.Npc(slot);
            long proceeds=0;int mail=0,rewards=0,jobs=0;
            if(profile&&!before.IsEmpty)
            {
                long savings=hosted.Profiles[slot].Savings-baseline.Profiles[slot].Savings;
                long held=hosted.Town.Shop.Proceeds[slot]-baseline.Town.Shop.Proceeds[slot];
                proceeds=held>0&&savings>long.MaxValue-held?long.MaxValue:Math.Max(0,savings+held);
                for(int i=0;i<OfflineJobLedger.RewardCapacity;i++)
                {var r=hosted.Town.Jobs.Reward(i);if(r.Owner==before&&r.PortableOnline&&baseline.Town.Jobs.FindReward(r.Id)<0)rewards++;}
                for(int i=0;i<hosted.Profiles[slot].MailCount;i++)
                {var m=hosted.Town.Jobs.Letter(slot,i);bool known=false;for(int j=0;j<baseline.Profiles[slot].MailCount;j++)if(baseline.Town.Jobs.Letter(slot,j).Id==m.Id){known=true;break;}if(!known)mail++;}
            }
            if(!profile)
            {
                await using(var count=Cmd(c,t,"SELECT count(*) FROM hosted_jobs WHERE host=$1 AND phase<=3 AND (requester=$2 OR target=$2)",device.Player,slot))jobs=(int)(long)(await count.ExecuteScalarAsync())!;
                await using(var count=Cmd(c,t,"SELECT count(*) FROM hosted_mail WHERE host=$1 AND recipient_identity=$2 AND NOT delivered AND NOT replied",device.Player,JobKey(before)))mail=(int)(long)(await count.ExecuteScalarAsync())!;
            }
            float x=profile?(local.HomePlaced(slot)?local.HomeX(slot)+3.5f:baseline.HomeX(slot)+3.5f):local.Town.Residents[slot].PosX;
            float z=profile?(local.HomePlaced(slot)?local.HomeZ(slot)+4.5f:baseline.HomeZ(slot)+4.5f):local.Town.Residents[slot].PosZ;
            entries.Add(new{index,kind=profile?"profile":"resident",approved=(review.Approved&(1<<index))!=0,
                previousName=profile?baseline.Profiles[slot].Name:baseline.Town.Residents[slot].Name,
                name=profile?local.Profiles[slot].Name:local.Town.Residents[slot].Name,
                previous=before.IsEmpty?"":JobKey(before),identity=after.IsEmpty?"":JobKey(after),
                x=(int)(x*1024),z=(int)(z*1024),room=profile?0:local.Town.Residents[slot].Space,proceeds,letters=mail,rewards,jobs,
                recipient=profile?review.Recipients[slot]:""});
        }
        var recipients=Enumerable.Range(0,GameState.ProfileCount).Where(p=>local.Profiles[p].Created)
            .Select(p=>new{profile=p,identity=JobKey(local.Town.Jobs.Player(p)),name=local.Profiles[p].Name,x=(int)((local.HomeX(p)+3.5f)*1024),z=(int)((local.HomeZ(p)+4.5f)*1024)}).ToArray();
        await t.CommitAsync();return new{id=review.Id,version=review.Version,sha256=review.Fingerprint,complete=review.Approved==changed,entries,recipients};
    }

    public async Task<object> DecideResidentReview(DevicePrincipal device,Guid lease,ResidentReviewOperation op)
    {
        if(op.Id==Guid.Empty||op.Review==Guid.Empty||op.Version<1||op.Index is <0 or >=10||op.Recipient is null||op.Recipient.Length>33)
            throw new GameError(400,"invalid-resident-review-operation");
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await LockTown(c,t,device.Player);await RequireActiveDevice(c,t,device);
        string fingerprint=Hash("resident-review:"+device.Town+":"+JsonSerializer.Serialize(op));
        if(await ReadPlacementReceipt(c,t,device.Player,op.Id,fingerprint) is {} receipt)return receipt;
        var hosted=(await Load(c,t,device.Player,false)).State;
        await RequirePlacementLease(c,t,device,lease,ReviewDeviceVillager(hosted,device));
        var review=await ReadResidentReview(c,t,device.Player);if(review.Id!=op.Review||review.Version!=op.Version)throw new GameError(409,"resident-review-changed");
        var local=review.State();var baseline=await ResidentBaseline(c,t,device.Player,review);
        if((OnlineResidentDecisions.Changes(local,baseline)&(1<<op.Index))==0)throw new GameError(400,"resident-change-not-found");
        string recipient="";
        if(op.Index<GameState.ProfileCount&&!baseline.Town.Jobs.Player(op.Index).IsEmpty&&local.Town.Jobs.FindPlayer(baseline.Town.Jobs.Player(op.Index))<0)
        {
            int target=local.Town.Jobs.FindPlayer(ParseJobKey(op.Recipient));
            if((uint)target>=GameState.ProfileCount||!local.Profiles[target].Created)throw new GameError(409,"resident-recipient-changed");
            recipient=op.Recipient;
        }
        else if(op.Recipient.Length!=0)throw new GameError(400,"unexpected-resident-recipient");
        int approved=review.Approved|(1<<op.Index),version=checked(review.Version+1);
        var recipients=review.Recipients;if(op.Index<GameState.ProfileCount)recipients[op.Index]=recipient;
        await using(var write=Cmd(c,t,"UPDATE offline_resident_reviews SET approved=$2,recipients=$3,version=$4 WHERE player=$1",device.Player,approved,recipients,version))await write.ExecuteNonQueryAsync();
        var result=new{id=review.Id,version,approved};await WritePlacementReceipt(c,t,device.Player,op.Id,fingerprint,result);
        await RequirePlacementLease(c,t,device,lease,ReviewDeviceVillager(hosted,device));await t.CommitAsync();return result;
    }

    // Acquire every affected account in the same Guid order as normal guest
    // commands. Recheck the set after locking: a new sender/job racing the
    // initial read causes a retry instead of taking a lock out of order.
    static async Task<List<Guid>> ResidentParticipants(NpgsqlConnection c,NpgsqlTransaction t,Guid owner)
    {
        var result=new HashSet<Guid>{owner};
        await using var read=Cmd(c,t,"""
            SELECT player FROM hosted_jobs WHERE host=$1 AND phase<=5
            UNION SELECT host FROM hosted_jobs WHERE player=$1 AND phase<=5
            UNION SELECT sender FROM hosted_mail WHERE host=$1 AND NOT replied
            UNION SELECT host FROM hosted_mail WHERE sender=$1 AND NOT replied
            """,owner);
        await using var rows=await read.ExecuteReaderAsync();while(await rows.ReadAsync())result.Add(rows.GetGuid(0));
        return result.OrderBy(id=>id).ToList();
    }

    public async Task<object> CompleteResidentReview(DevicePrincipal device,Guid lease,ResidentReviewCompletion op)
    {
        if(op.Id==Guid.Empty||op.Review==Guid.Empty||op.Version<1)throw new GameError(400,"invalid-resident-review-operation");
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        var participants=await ResidentParticipants(c,t,device.Player);
        foreach(var player in participants)await LockTown(c,t,player);
        if((await ResidentParticipants(c,t,device.Player)).Any(p=>!participants.Contains(p)))throw new GameError(409,"resident-review-changed");
        await RequireActiveDevice(c,t,device);
        string fingerprint=Hash("resident-review-complete:"+device.Town+":"+JsonSerializer.Serialize(op));
        if(await ReadPlacementReceipt(c,t,device.Player,op.Id,fingerprint) is {} receipt)return receipt;
        var hosted=(await Load(c,t,device.Player,false)).State;string villager=ReviewDeviceVillager(hosted,device);
        await RequirePlacementLease(c,t,device,lease,villager);
        var review=await ReadResidentReview(c,t,device.Player);if(review.Id!=op.Review||review.Version!=op.Version)throw new GameError(409,"resident-review-changed");
        var baseline=await ResidentBaseline(c,t,device.Player,review);var local=review.State();
        var recipients=review.Recipients.Select(s=>s.Length==0?default:ParseJobKey(s)).ToArray();
        var decisions=new OnlineResidentDecisions(review.Approved,recipients);
        try{OnlineTownMerge.Apply(local,baseline,hosted,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),decisions);}
        catch(InvalidOperationException){throw new GameError(409,"resident-review-capacity");}
        await SettleResidentEffects(c,t,device.Player,baseline,local,decisions);
        await AcceptOfflineCheckpoint(c,t,device.Player,review.Parent,new(local,review.Commit,review.Records,review.Fingerprint),CancellationToken.None);
        await RequirePlacementLease(c,t,device,lease,villager);
        await using(var clear=Cmd(c,t,"DELETE FROM offline_resident_reviews WHERE player=$1",device.Player))await clear.ExecuteNonQueryAsync();
        await using(var reopen=Cmd(c,t,"DELETE FROM online_town_closures WHERE player=$1",device.Player))await reopen.ExecuteNonQueryAsync();
        // The selected resident may have changed. End the old identity's lease
        // atomically; the console can immediately review placements anew.
        await using(var clear=Cmd(c,t,"DELETE FROM town_review_leases WHERE host=$1 AND id=$2",device.Player,lease))await clear.ExecuteNonQueryAsync();
        var result=new{id=review.Id,revision=checked(review.Parent+1),sha256=review.Fingerprint};
        await WritePlacementReceipt(c,t,device.Player,op.Id,fingerprint,result);await t.CommitAsync();return result;
    }

    async Task SettleResidentEffects(NpgsqlConnection c,NpgsqlTransaction t,Guid owner,GameState baseline,GameState local,OnlineResidentDecisions decisions)
    {
        var changed=new List<int>();for(int n=0;n<Town.SavedVillagerCount;n++)if(local.Town.Jobs.Npc(n)!=baseline.Town.Jobs.Npc(n))changed.Add(n);
        foreach(int npc in changed)
        {
            var affected=new List<(Guid Id,Guid Player,int Profile,int Pocket,int Phase,string? Reward)>();
            await using(var read=Cmd(c,t,"SELECT id,player,profile,parcel_pocket,phase,reward_identity FROM hosted_jobs WHERE host=$1 AND phase<=5 AND (requester=$2 OR target=$2)",owner,npc))
            await using(var rows=await read.ExecuteReaderAsync())while(await rows.ReadAsync())affected.Add((rows.GetGuid(0),rows.GetGuid(1),rows.GetInt32(2),rows.GetInt32(3),rows.GetInt32(4),rows.IsDBNull(5)?null:rows.GetString(5)));
            foreach(var job in affected)
            {
                var player=job.Player==owner?local:(await Load(c,t,job.Player,false)).State;
                if(job.Pocket>=0)
                {
                    if(player.ActiveProfile!=job.Profile||player.Pockets[job.Pocket]!=JobContent.Parcel||player.IsJobItem(job.Pocket))throw new GameError(409,"parcel-binding-changed");
                    player.Pockets[job.Pocket]=0;
                    if(job.Player!=owner){await Save(c,t,job.Player,player);var updated=await Load(c,t,job.Player,false);await AppendHostedEvent(c,t,owner,job.Player,2,job.Player,InventoryPacket(player,updated.Revision));}
                }
                int phase=job.Reward!=null&&player.Town.Jobs.FindReward(ParseJobKey(job.Reward))>=0?5:job.Phase<=3?7:6;
                await using var retire=Cmd(c,t,"UPDATE hosted_jobs SET phase=$2,parcel_pocket=-1,version=version+1 WHERE id=$1",job.Id,phase);await retire.ExecuteNonQueryAsync();
            }
            var departed=baseline.Town.Jobs.Npc(npc);int present=local.Town.Jobs.FindNpc(departed);
            await using var mail=present<0
                ?Cmd(c,t,"UPDATE hosted_mail SET returned=NOT delivered,due=CASE WHEN delivered THEN due ELSE least(due,$3) END WHERE host=$1 AND recipient_identity=$2 AND NOT replied",owner,JobKey(departed),DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                :Cmd(c,t,"UPDATE hosted_mail SET recipient_npc=$3 WHERE host=$1 AND recipient_identity=$2 AND NOT replied",owner,JobKey(departed),present);
            await mail.ExecuteNonQueryAsync();
        }
        for(int p=0;p<GameState.ProfileCount;p++)
        {
            var previous=baseline.Town.Jobs.Player(p);if(previous.IsEmpty||local.Town.Jobs.FindPlayer(previous)>=0)continue;
            var replacement=decisions.Resolve(local,baseline,previous);int target=local.Town.Jobs.FindPlayer(replacement);
            await using(var mail=Cmd(c,t,"UPDATE hosted_mail SET recipient_identity=$3,recipient_profile=$4 WHERE host=$1 AND recipient_identity=$2 AND recipient_npc<0 AND NOT replied",owner,JobKey(previous),JobKey(replacement),target))await mail.ExecuteNonQueryAsync();
            await using(var mail=Cmd(c,t,"UPDATE hosted_mail SET sender_identity=$3 WHERE sender=$1 AND sender_identity=$2 AND NOT replied",owner,JobKey(previous),JobKey(replacement)))await mail.ExecuteNonQueryAsync();
            await using(var jobs=Cmd(c,t,"UPDATE hosted_jobs SET phase=CASE WHEN phase<=3 THEN 7 ELSE 6 END,version=version+1 WHERE player=$1 AND villager=$2 AND phase<=5",owner,JobKey(previous)))await jobs.ExecuteNonQueryAsync();
        }
        await using(var clear=Cmd(c,t,"DELETE FROM hosted_job_conversations WHERE host=$1",owner))await clear.ExecuteNonQueryAsync();
    }
}
