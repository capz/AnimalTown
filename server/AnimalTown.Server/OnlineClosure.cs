using System.Text.Json;

sealed record OnlineClosureRequest(Guid Id,long Revision,string Fingerprint,bool WholeTown=false);
sealed record OnlineRetirementRequest(Guid Id);

sealed partial class GameDatabase
{
    public async Task<object> CloseOnlineTown(DevicePrincipal device,OnlineClosureRequest request)
    {
        if(request.Id==Guid.Empty||request.Revision<1||request.Fingerprint is not{Length:64})throw new GameError(400,"invalid-online-closure");
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await LockTown(c,t,device.Player);await RequireActiveDevice(c,t,device);
        string hash=Hash("online-closure:"+device.Town+":"+JsonSerializer.Serialize(request));
        if(await ReadPlacementReceipt(c,t,device.Player,request.Id,hash) is {} prior)return prior;
        await using(var head=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM online_card_heads WHERE player=$1 AND revision=$2 AND fingerprint=$3)",device.Player,request.Revision,request.Fingerprint))
            if(await head.ExecuteScalarAsync() is not true)throw new GameError(409,"card-lineage-changed");
        var state=(await Load(c,t,device.Player,false)).State;ReviewDeviceVillager(state,device);
        if(!request.WholeTown&&state.Profiles.Count(p=>p.Created)!=1)throw new GameError(409,"last-resident-required");
        // Guest earnings remain in their private receipts/reward ledgers. Wait
        // for returns and correspondence instead of invalidating either.
        await using(var pending=Cmd(c,t,"""
            SELECT EXISTS(SELECT 1 FROM gates WHERE host=$1)
              OR EXISTS(SELECT 1 FROM online_travel WHERE (player=$1 OR host=$1) AND phase<3)
              OR EXISTS(SELECT 1 FROM guest_placements WHERE host=$1)
              OR EXISTS(SELECT 1 FROM offline_resident_reviews WHERE player=$1)
              OR EXISTS(SELECT 1 FROM town_review_leases WHERE host=$1 AND expires>clock_timestamp())
              OR EXISTS(SELECT 1 FROM hosted_mail WHERE (host=$1 OR sender=$1) AND NOT replied)
              OR EXISTS(SELECT 1 FROM hosted_jobs WHERE host=$1 AND parcel_pocket>=0)
            """,device.Player))
            if(await pending.ExecuteScalarAsync() is true)throw new GameError(409,"online-closure-pending");
        await using(var retire=Cmd(c,t,"UPDATE hosted_jobs SET phase=7,version=version+1 WHERE host=$1 AND phase<=3",device.Player))await retire.ExecuteNonQueryAsync();
        await using(var close=Cmd(c,t,"""
            INSERT INTO online_town_closures(player,town_identity,revision,fingerprint) VALUES($1,$2,$3,$4)
            ON CONFLICT(player) DO UPDATE SET revision=excluded.revision,fingerprint=excluded.fingerprint
            """,device.Player,device.Town,request.Revision,request.Fingerprint))await close.ExecuteNonQueryAsync();
        var result=new{closed=true,revision=request.Revision,sha256=request.Fingerprint};
        await WritePlacementReceipt(c,t,device.Player,request.Id,hash,result);await t.CommitAsync();return result;
    }
    public async Task<object> RetireOnlineTown(DevicePrincipal device,OnlineRetirementRequest request)
    {
        if(request.Id==Guid.Empty)throw new GameError(400,"invalid-online-retirement");
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        var participants=await ResidentParticipants(c,t,device.Player);foreach(var player in participants)await LockTown(c,t,player);
        if((await ResidentParticipants(c,t,device.Player)).Any(p=>!participants.Contains(p)))throw new GameError(409,"resident-review-changed");
        await RequireActiveDevice(c,t,device);
        string hash=Hash("online-retirement:"+device.Town+":"+request.Id);
        if(await ReadPlacementReceipt(c,t,device.Player,request.Id,hash) is {} prior)return prior;
        await using(var closed=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM online_town_closures WHERE player=$1 AND town_identity=$2)",device.Player,device.Town))
            if(await closed.ExecuteScalarAsync() is not true)throw new GameError(409,"close-online-town-first");
        bool retired;
        await using(var read=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM retired_online_towns WHERE player=$1 AND town_identity=$2)",device.Player,device.Town))retired=await read.ExecuteScalarAsync() is true;
        if(!retired)
        {
            var state=(await Load(c,t,device.Player,false)).State;
            if(state.Town.Jobs.TownId.ToString("X16")!=device.Town)throw new GameError(409,"device-town-mismatch");
            // Closure has already settled returns/mail/placements. Preserve the
            // old owner's canonical checkpoint for recovery and tombstone its
            // identity; other players keep their own purchases and rewards.
            var records=AnimalTown.SplitSaveCodec.Encode(state);var commit=AnimalTown.SplitSaveCodec.EncodeCommit(state,records);
            await using(var archive=Cmd(c,t,"INSERT INTO retired_online_towns(player,town_identity,save_commit,save_records) VALUES($1,$2,$3,$4)",device.Player,device.Town,commit,records))await archive.ExecuteNonQueryAsync();
            await using(var jobs=Cmd(c,t,"UPDATE hosted_jobs SET phase=CASE WHEN phase<=3 THEN 7 ELSE 6 END,version=version+1 WHERE (host=$1 OR player=$1) AND phase<=5",device.Player))await jobs.ExecuteNonQueryAsync();
            await using(var visits=Cmd(c,t,"UPDATE visits SET ended=true WHERE (host=$1 OR guest=$1) AND NOT ended",device.Player))await visits.ExecuteNonQueryAsync();
            await using(var actors=Cmd(c,t,"DELETE FROM hosted_actors WHERE host=$1 OR player=$1",device.Player))await actors.ExecuteNonQueryAsync();
            foreach(string table in new[]{"online_card_heads","offline_town_checkpoints","checkpoint_uploads","hosted_personal_calendar","initial_town_imports"})
            {await using var clear=Cmd(c,t,"DELETE FROM "+table+" WHERE player=$1",device.Player);await clear.ExecuteNonQueryAsync();}
            await WriteParts(c,t,device.Player,new AnimalTown.GameState(state.Town.Seed),false);
            await using(var reset=Cmd(c,t,"UPDATE players SET revision=0 WHERE id=$1",device.Player))await reset.ExecuteNonQueryAsync();
        }
        var result=new{retired=true,town=device.Town};await WritePlacementReceipt(c,t,device.Player,request.Id,hash,result);await t.CommitAsync();return result;
    }
}
