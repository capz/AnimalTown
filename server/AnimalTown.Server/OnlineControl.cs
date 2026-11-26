using Npgsql;

static class OnlineControlRoutes
{
    internal static void MapOnlineControl(this WebApplication app,GameDatabase database)
    {
        DevicePrincipal Device(HttpContext c)=>(DevicePrincipal)c.Items["device"]!;
        app.MapGet("/v1/online/card",(Delegate)((HttpContext c)=>database.OnlineCardHead(Device(c)))).WithMetadata(ApiAccess.DeviceOnly);
        app.MapGet("/v1/online/friends",(HttpContext c,int? page)=>database.OpenOnlineFriends(Device(c),page??0)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapGet("/v1/online/gate",(Delegate)((HttpContext c)=>database.OnlineGateStatus(Device(c)))).WithMetadata(ApiAccess.DeviceOnly);
    }
}

sealed partial class GameDatabase
{
    public async Task<object> OnlineCardHead(DevicePrincipal device)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await LockTown(c,t,device.Player);await RequireActiveDevice(c,t,device);await EnsureOnlineCardHead(c,t,device.Player);
        long revision=0;string sha256="";
        await using(var read=Cmd(c,t,"SELECT revision,fingerprint FROM online_card_heads WHERE player=$1",device.Player))
        await using(var row=await read.ExecuteReaderAsync())if(await row.ReadAsync()){revision=row.GetInt64(0);sha256=row.GetString(1);}
        object? pending=null;
        await using(var read=Cmd(c,t,"SELECT id,phase,profile,host,visit,parent_revision FROM online_travel WHERE player=$1 AND town_identity=$2 AND phase<3",device.Player,device.Town))
        await using(var row=await read.ExecuteReaderAsync())if(await row.ReadAsync())pending=new{id=row.GetGuid(0),phase=row.GetInt32(1),profile=row.GetInt32(2),host=row.GetGuid(3),visit=row.GetGuid(4),parentRevision=row.GetInt64(5)};
        await t.CommitAsync();return new{revision,sha256,pending};
    }

    public async Task<object> OpenOnlineFriends(DevicePrincipal device,int page)
    {
        if(page<0||page>4095)throw new GameError(400,"invalid-friends-page");
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        await RequireActiveDevice(c,t,device);var friends=new List<object>();
        await using(var read=Cmd(c,t,"""
            SELECT p.id,p.name,(SELECT count(*) FROM visits v WHERE v.host=p.id AND NOT v.ended AND v.expires>clock_timestamp())::int
            FROM friends f JOIN players p ON p.id=f.recipient JOIN gates g ON g.host=p.id
            WHERE f.sender=$1 AND g.expires>clock_timestamp()
              AND EXISTS(SELECT 1 FROM initial_town_imports i WHERE i.player=p.id)
              AND EXISTS(SELECT 1 FROM friends r WHERE r.sender=p.id AND r.recipient=$1)
              AND NOT EXISTS(SELECT 1 FROM blocks b WHERE b.blocker=$1 AND b.blocked=p.id OR b.blocker=p.id AND b.blocked=$1)
              AND NOT EXISTS(SELECT 1 FROM gate_removals x WHERE x.host=p.id AND x.guest=$1)
            ORDER BY p.id LIMIT 17 OFFSET $2
            """,device.Player,page*16))
        await using(var rows=await read.ExecuteReaderAsync())while(await rows.ReadAsync())friends.Add(new{id=rows.GetGuid(0),name=rows.GetString(1),guests=rows.GetInt32(2)});
        bool more=friends.Count>16;if(more)friends.RemoveAt(16);
        await t.CommitAsync();return new{friends,more};
    }

    public async Task<object> OnlineGateStatus(DevicePrincipal device)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();await RequireActiveDevice(c,t,device);
        bool open;await using(var read=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM gates WHERE host=$1 AND expires>clock_timestamp())",device.Player))open=await read.ExecuteScalarAsync() is true;
        var guests=new List<object>();
        await using(var read=Cmd(c,t,"SELECT v.id,p.name FROM visits v JOIN players p ON p.id=v.guest WHERE v.host=$1 AND NOT v.ended AND v.expires>clock_timestamp() ORDER BY v.id LIMIT 2",device.Player))
        await using(var rows=await read.ExecuteReaderAsync())while(await rows.ReadAsync())guests.Add(new{id=rows.GetGuid(0),name=rows.GetString(1)});
        await t.CommitAsync();return new{open,guests};
    }
}
