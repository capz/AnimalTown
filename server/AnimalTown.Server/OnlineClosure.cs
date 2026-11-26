using System.Text.Json;

sealed record OnlineClosureRequest(Guid Id,long Revision,string Fingerprint);

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
        if(state.Profiles.Count(p=>p.Created)!=1)throw new GameError(409,"last-resident-required");
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
}
