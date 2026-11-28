using AnimalTown;
using AnimalTown.Online;
using Npgsql;

readonly record struct HostedAuthority(Guid Host, Guid Player, Guid Visit, Guid Connection,
    Guid Worker, uint Epoch, DevicePrincipal Device);
readonly record struct HostedSeat(byte Slot, int Room, float X, float Z, ushort Facing);
sealed record HostedEvent(long Cursor, int Kind, byte[] Payload);
sealed record HostedReplay(long Cursor, bool Resnapshot, List<HostedEvent> Events);

sealed partial class GameDatabase
{
    internal async Task<uint> ClaimHostedTown(Guid host, Guid openedGate, Guid worker)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, host);
        await using(var opened=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM gates WHERE host=$1 AND generation=$2 AND expires>clock_timestamp())",host,openedGate))
            if(await opened.ExecuteScalarAsync() is not true)throw new GameError(403,"gate-closed");
        long? reuse=null;
        await using(var current=Cmd(c,t,"SELECT gate,epoch,worker,expires>clock_timestamp() FROM hosted_towns WHERE host=$1",host))
        await using(var row=await current.ExecuteReaderAsync())
        {
            if(await row.ReadAsync()&&row.GetBoolean(3))
            {
                if(row.GetGuid(2)!=worker)throw new GameError(503,"town-worker-busy");
                if(row.GetGuid(0)==openedGate)reuse=row.GetInt64(1);
            }
        }
        if(reuse is long existing)
        {
            await using var renew=Cmd(c,t,"UPDATE hosted_towns SET expires=clock_timestamp()+interval '10 seconds' WHERE host=$1",host);
            await renew.ExecuteNonQueryAsync();await t.CommitAsync();return checked((uint)existing);
        }
        await using var cmd = Cmd(c, t, """
            INSERT INTO hosted_towns(host,gate,epoch,worker,expires)
            VALUES($1,$2,nextval('hosted_epoch'),$3,clock_timestamp()+interval '10 seconds')
            ON CONFLICT(host) DO UPDATE SET gate=excluded.gate,worker=excluded.worker,
              epoch=CASE WHEN hosted_towns.gate=$2 AND hosted_towns.worker=$3 AND hosted_towns.expires>clock_timestamp()
                         THEN hosted_towns.epoch ELSE excluded.epoch END,
              expires=excluded.expires
            WHERE hosted_towns.worker=$3 OR hosted_towns.expires<=clock_timestamp()
            RETURNING epoch
            """, host, openedGate, worker);
        if (await cmd.ExecuteScalarAsync() is not long epoch) throw new GameError(503, "town-worker-busy");
        await t.CommitAsync();
        return checked((uint)epoch);
    }

    static async Task RequireHostedWriter(NpgsqlConnection c, NpgsqlTransaction t, HostedAuthority authority)
    {
        await using var command = Cmd(c, t, """
            SELECT EXISTS(SELECT 1 FROM hosted_towns h JOIN gates g ON g.host=h.host AND g.generation=h.gate
            WHERE h.host=$1 AND h.worker=$2 AND h.epoch=$3 AND h.expires>clock_timestamp() AND g.expires>clock_timestamp())
            """, authority.Host, authority.Worker, (long)authority.Epoch);
        if (await command.ExecuteScalarAsync() is not true) throw new GameError(409, "town-epoch-changed");
    }

    static async Task<HostedSeat> RequireHostedActor(NpgsqlConnection c, NpgsqlTransaction t, HostedAuthority authority)
    {
        await RequireActiveDevice(c, t, authority.Device);
        await RequireHostedWriter(c, t, authority);
        if (authority.Player != authority.Host)
            await RequirePlacementVisit(c, t, authority.Host, authority.Player, authority.Visit);
        await using var command = Cmd(c, t, """
            SELECT slot,room,x,z,facing FROM hosted_actors
            WHERE host=$1 AND player=$2 AND connection=$3 AND device=$4 AND generation=$5 AND visit=$6
              AND expires>clock_timestamp() AND updated>clock_timestamp()-interval '3 seconds'
            """, authority.Host, authority.Player, authority.Connection, authority.Device.Id,
            authority.Device.Generation, authority.Visit);
        await using var row = await command.ExecuteReaderAsync();
        if (!await row.ReadAsync()) throw new GameError(409, "gameplay-connection-changed");
        return new((byte)row.GetInt32(0), row.GetInt32(1), row.GetFloat(2), row.GetFloat(3), (ushort)row.GetInt32(4));
    }

    internal async Task<HostedSeat> BindHostedActor(HostedAuthority authority, HostedSeat arrival)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        if (authority.Host == authority.Player) await LockTown(c, t, authority.Host);
        else await LockVisitParticipants(c, t, authority.Host, authority.Player);
        await RequireActiveDevice(c, t, authority.Device);
        await RequireHostedWriter(c, t, authority);
        if (authority.Player != authority.Host)
            await RequirePlacementVisit(c, t, authority.Host, authority.Player, authority.Visit);
        await using (var clean = Cmd(c, t, "DELETE FROM hosted_actors WHERE host=$1 AND expires<=clock_timestamp()", authority.Host))
            await clean.ExecuteNonQueryAsync();
        HostedSeat seat = arrival;
        long joinedCursor;
        await using(var head=Cmd(c,t,"SELECT cursor FROM hosted_towns WHERE host=$1",authority.Host))joinedCursor=(long)(await head.ExecuteScalarAsync())!;
        bool present = false;
        var used = new bool[3];
        await using (var read = Cmd(c, t, "SELECT player,slot,room,x,z,facing,visit,joined_cursor FROM hosted_actors WHERE host=$1", authority.Host))
        await using (var rows = await read.ExecuteReaderAsync())
            while (await rows.ReadAsync())
            {
                int slot = rows.GetInt32(1); used[slot] = true;
                if (rows.GetGuid(0) == authority.Player)
                {
                    seat = rows.GetGuid(6) == authority.Visit
                        ? new((byte)slot, rows.GetInt32(2), rows.GetFloat(3), rows.GetFloat(4), (ushort)rows.GetInt32(5))
                        : arrival with { Slot = (byte)slot };
                    present = true;
                    if(rows.GetGuid(6)==authority.Visit)joinedCursor=rows.GetInt64(7);
                }
            }
        if (!present && authority.Host != authority.Player)
        {
            int slot = !used[1] ? 1 : !used[2] ? 2 : -1;
            if (slot < 0) throw new GameError(409, "town-full");
            seat = arrival with { Slot = (byte)slot };
        }
        await using (var write = Cmd(c, t, """
            INSERT INTO hosted_actors(host,player,slot,visit,device,generation,connection,room,x,z,facing,joined_cursor,expires)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,clock_timestamp()+interval '60 seconds')
            ON CONFLICT(host,player) DO UPDATE SET slot=excluded.slot,visit=excluded.visit,device=excluded.device,
              generation=excluded.generation,connection=excluded.connection,room=excluded.room,
              x=excluded.x,z=excluded.z,facing=excluded.facing,joined_cursor=excluded.joined_cursor,expires=excluded.expires,updated=clock_timestamp()
            """, authority.Host, authority.Player, (int)seat.Slot, authority.Visit, authority.Device.Id,
            authority.Device.Generation, authority.Connection, seat.Room, seat.X, seat.Z, (int)seat.Facing,joinedCursor))
            await write.ExecuteNonQueryAsync();
        await t.CommitAsync();
        return seat;
    }

    internal async Task RefreshHostedActor(HostedAuthority authority, HostedSeat pose)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, authority.Host);
        await RequireHostedWriter(c, t, authority);
        await using (var renew = Cmd(c, t, """
            UPDATE hosted_towns SET expires=clock_timestamp()+interval '10 seconds' WHERE host=$1 AND worker=$2 AND epoch=$3
            """, authority.Host, authority.Worker, (long)authority.Epoch)) await renew.ExecuteNonQueryAsync();
        await using (var update = Cmd(c, t, """
            UPDATE hosted_actors SET room=$4,x=$5,z=$6,facing=$7,updated=clock_timestamp(),expires=clock_timestamp()+interval '60 seconds'
            WHERE host=$1 AND player=$2 AND connection=$3
            """, authority.Host, authority.Player, authority.Connection, pose.Room, pose.X, pose.Z, (int)pose.Facing))
            if (await update.ExecuteNonQueryAsync() != 1) throw new GameError(409, "gameplay-connection-changed");
        await t.CommitAsync();
    }

    // The caller holds the host row lock; cursor, mutation and receipt commit together.
    static async Task<long> AppendHostedEvent(NpgsqlConnection c, NpgsqlTransaction t, Guid host,
        Guid? audience, int kind, Guid? actor, byte[] payload)
    {
        if (payload.Length is < 1 or > 4083) throw new GameError(413, "event-too-large");
        long cursor;
        await using (var advance = Cmd(c, t, "UPDATE hosted_towns SET cursor=cursor+1 WHERE host=$1 RETURNING cursor", host))
            cursor = await advance.ExecuteScalarAsync() is long value ? value : throw new GameError(409, "hosted-town-required");
        await using (var insert = Cmd(c, t, "INSERT INTO hosted_events(host,cursor,audience,kind,actor,payload) VALUES($1,$2,$3,$4,$5,$6)",
            host, cursor, (object?)audience ?? DBNull.Value, kind, (object?)actor ?? DBNull.Value, payload))
            await insert.ExecuteNonQueryAsync();
        await using (var trim = Cmd(c, t, "DELETE FROM hosted_events WHERE host=$1 AND cursor<=$2", host, cursor - 1024))
            await trim.ExecuteNonQueryAsync();
        return cursor;
    }

    internal async Task<HostedReplay> ReplayHostedEvents(HostedAuthority authority, long after)
    {
        if (after < 0) throw new GameError(400, "invalid-event-cursor");
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await RequireHostedActor(c, t, authority);
        long head;
        await using (var read = Cmd(c, t, "SELECT cursor FROM hosted_towns WHERE host=$1", authority.Host))
            head = (long)(await read.ExecuteScalarAsync())!;
        if (after > head || head - after > 1024) return new(head, true, []);
        var events = new List<HostedEvent>(16);
        long cursor = after;
        await using (var read = Cmd(c, t, """
            SELECT e.cursor,e.kind,e.payload,
              (e.audience IS NULL OR e.audience=$2) AND
              (e.kind<>4 OR e.cursor>(SELECT joined_cursor FROM hosted_actors WHERE host=$1 AND player=$2)) AND
              (e.kind<>4 OR NOT EXISTS(SELECT 1 FROM hosted_mutes m WHERE m.player=$2 AND m.muted=e.actor)) AND
              (e.kind<>4 OR NOT EXISTS(SELECT 1 FROM blocks b WHERE (b.blocker=$2 AND b.blocked=e.actor) OR (b.blocked=$2 AND b.blocker=e.actor)))
            FROM hosted_events e WHERE e.host=$1 AND e.cursor>$3 ORDER BY e.cursor LIMIT 4
            """, authority.Host, authority.Player, after))
        await using (var rows = await read.ExecuteReaderAsync())
            while (await rows.ReadAsync())
            {
                cursor = rows.GetInt64(0);
                // Hidden events advance the same cursor without disclosing their contents.
                events.Add(rows.GetBoolean(3) ? new(cursor, rows.GetInt32(1), rows.GetFieldValue<byte[]>(2)) : new(cursor, 0, [0]));
            }
        await t.CommitAsync();
        return new(cursor, false, events);
    }

    internal async Task<GameState> LoadHostedWorld(Guid host)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var world = (await Load(c, t, host, false)).State;
        ProjectPlacements(world, await ReadPlacements(c, t, host, Guid.Empty, PlacementLimit + 1));
        await t.CommitAsync(); return world;
    }

    internal async Task<byte[]> HostedRoster(HostedAuthority authority)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await RequireHostedActor(c, t, authority);
        var members = new List<(Guid Player, byte Slot)>(3);
        await using (var read = Cmd(c, t, "SELECT player,slot FROM hosted_actors WHERE host=$1 AND expires>clock_timestamp() AND updated>clock_timestamp()-interval '3 seconds' ORDER BY slot", authority.Host))
        await using (var rows = await read.ExecuteReaderAsync())
            while (await rows.ReadAsync()) members.Add((rows.GetGuid(0), (byte)rows.GetInt32(1)));
        byte[] packet = Packet(w =>
        {
            w.Byte(3); w.Int(members.Count);
            // States are read below, outside the synchronous packet callback.
        });
        var bytes = new byte[512]; var writer = new OnlinePacketWriter(bytes); writer.Bytes(packet, 0, packet.Length);
        foreach (var member in members)
        {
            var player = (await Load(c, t, member.Player, false)).State;
            writer.Byte(member.Slot); writer.Id(WireId(member.Player)); writer.Text(player.Name, 32);
            writer.Int(player.Species); writer.Int(player.Palette); writer.Int(player.Shirt);
        }
        await t.CommitAsync(); return bytes[..writer.Count];
    }
}
