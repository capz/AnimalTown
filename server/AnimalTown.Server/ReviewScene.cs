using System.Security.Cryptography;
using AnimalTown.Online;

sealed partial class GameDatabase
{
    public async Task<object> PrepareReviewScene(DevicePrincipal device,Guid lease)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await LockTown(c,t,device.Player);await RequireActiveDevice(c,t,device);
        var world=(await Load(c,t,device.Player,false)).State;
        await RequirePlacementLease(c,t,device,lease,ReviewDeviceVillager(world,device));
        // Resident changes inspect the actual uploaded offline town; ordinary
        // placement review inspects the current hosted town plus pending items.
        await using(var pending=Cmd(c,t,"SELECT save_commit,save_records FROM offline_resident_reviews WHERE player=$1",device.Player))
        await using(var row=await pending.ExecuteReaderAsync())if(await row.ReadAsync())world=AnimalTown.SplitSaveCodec.DecodeCommitted(row.GetFieldValue<byte[]>(0),row.GetFieldValue<byte[][]>(1));
        ProjectPlacements(world,await ReadPlacements(c,t,device.Player,Guid.Empty,PlacementLimit+1));
        var bytes=new byte[OnlineTownProjection.MaximumBytes];int length=OnlineTownProjection.Write(world,bytes);bytes=bytes[..length];
        await using(var write=Cmd(c,t,"UPDATE town_review_leases SET scene=$3 WHERE host=$1 AND id=$2",device.Player,lease,bytes))await write.ExecuteNonQueryAsync();
        await t.CommitAsync();return new{length,sha256=Convert.ToHexString(SHA256.HashData(bytes))};
    }
    public async Task<byte[]> ReadReviewScene(DevicePrincipal device,Guid lease,string sha256,int offset,int count)
    {
        if(offset<0||count is <1 or >16384||sha256 is not {Length:64})throw new GameError(400,"invalid-review-scene-page");
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await LockTown(c,t,device.Player);await RequireActiveDevice(c,t,device);
        var state=(await Load(c,t,device.Player,false)).State;
        await RequirePlacementLease(c,t,device,lease,ReviewDeviceVillager(state,device));
        await using var read=Cmd(c,t,"SELECT scene FROM town_review_leases WHERE host=$1 AND id=$2",device.Player,lease);
        if(await read.ExecuteScalarAsync() is not byte[] bytes||Convert.ToHexString(SHA256.HashData(bytes))!=sha256)
            throw new GameError(409,"review-scene-changed");
        if(offset>bytes.Length)throw new GameError(400,"invalid-review-scene-page");
        return bytes.AsSpan(offset,Math.Min(count,bytes.Length-offset)).ToArray();
    }
}
