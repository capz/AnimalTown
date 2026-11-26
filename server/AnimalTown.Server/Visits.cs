using Npgsql;

sealed partial class GameDatabase
{
    const int GuestLimit = 2;
    const int ReconnectSeconds = 60;
    const int ReservationSeconds = 120;

    // Gate transitions and guest economy writes take the same town row lock.
    static async Task LockTown(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid town)
    {
        await using var command = Cmd(connection, transaction,
            "SELECT id FROM players WHERE id=$1 FOR UPDATE", town);
        if (await command.ExecuteScalarAsync() is not Guid)
            throw new GameError(404, "player-not-found");
    }

    static async Task LockVisitParticipants(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid host, Guid guest)
    {
        Guid first = host.CompareTo(guest) < 0 ? host : guest;
        await LockTown(connection, transaction, first);
        await LockTown(connection, transaction, first == host ? guest : host);
    }

    const string GateAccess = """
        EXISTS(SELECT 1 FROM friends WHERE sender=$1 AND recipient=$2)
        AND EXISTS(SELECT 1 FROM friends WHERE sender=$2 AND recipient=$1)
        AND NOT EXISTS(SELECT 1 FROM blocks WHERE (blocker=$1 AND blocked=$2) OR (blocker=$2 AND blocked=$1))
        AND EXISTS(SELECT 1 FROM gates WHERE host=$1 AND expires>now())
        AND NOT EXISTS(SELECT 1 FROM gate_removals WHERE host=$1 AND guest=$2)
        """;

    static async Task RequireOpenGate(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid host, Guid guest)
    {
        await using var command = Cmd(connection, transaction, "SELECT " + GateAccess, host, guest);
        if (await command.ExecuteScalarAsync() is not true)
            throw new GameError(403, "gate-or-friendship-required");
    }

    async Task RequireManagedGateOwner(NpgsqlConnection connection, NpgsqlTransaction transaction, DevicePrincipal device)
    {
        await RequireActiveDevice(connection, transaction, device);
        await using var command = Cmd(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM initial_town_imports WHERE player=$1 AND town_identity=$2)",
            device.Player, device.Town);
        if (await command.ExecuteScalarAsync() is not true)
            throw new GameError(409, "town-checkpoint-required");
    }

    public async Task<object> OpenGate(Guid host, DevicePrincipal? device = null)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockTown(connection, transaction, host);
        if (device is not null) await RequireManagedGateOwner(connection, transaction, device);
        await using(var closed=Cmd(connection,transaction,"SELECT EXISTS(SELECT 1 FROM online_town_closures WHERE player=$1)",host))
            if(await closed.ExecuteScalarAsync() is true)throw new GameError(409,"online-town-closed");
        await using var command = Cmd(connection, transaction, """
            INSERT INTO gates(host,expires) VALUES($1,'infinity')
            ON CONFLICT(host) DO UPDATE SET expires=excluded.expires,
              generation=CASE WHEN gates.expires<=now() THEN excluded.generation ELSE gates.generation END
            """, host);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new { ok = true, open = true };
    }

    public async Task<object> CloseGate(Guid host, DevicePrincipal? device = null)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockTown(connection, transaction, host);
        if (device is not null) await RequireManagedGateOwner(connection, transaction, device);
        Guid? closedGate;
        await using (var command = Cmd(connection, transaction, "DELETE FROM gates WHERE host=$1 RETURNING generation", host))
            closedGate = (Guid?)await command.ExecuteScalarAsync();
        await using (var command = Cmd(connection, transaction, "UPDATE visits SET ended=true WHERE host=$1 AND NOT ended", host))
            await command.ExecuteNonQueryAsync();
        await using (var command = Cmd(connection, transaction, "DELETE FROM tents WHERE host=$1", host))
            await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        if (closedGate is Guid gateId) LiveTransport?.CloseTown(host, gateId);
        return new { ok = true, open = false };
    }

    async Task CheckVisit(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid guest, Guid host)
    {
        await using var command = Cmd(connection, transaction, "SELECT " + GateAccess + """
             AND EXISTS(SELECT 1 FROM visits WHERE host=$1 AND guest=$2 AND NOT ended AND expires>now())
            """, host, guest);
        if (await command.ExecuteScalarAsync() is not true)
            throw new GameError(403, "active-visit-required");
    }

    public async Task<object> Visit(Guid guest, Guid host, DevicePrincipal? device = null)
    {
        if (host == guest) throw new GameError(400, "own-town");
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockVisitParticipants(connection, transaction, host, guest);
        if (device is not null) await RequireActiveDevice(connection, transaction, device);
        await RequireOpenGate(connection, transaction, host, guest);
        await using (var command = Cmd(connection, transaction,
            "UPDATE visits SET ended=true WHERE guest=$1 AND NOT ended AND expires<=now()", guest))
            await command.ExecuteNonQueryAsync();
        await using (var command = Cmd(connection, transaction, "SELECT id,host,ceil(EXTRACT(EPOCH FROM expires-now()))::int FROM visits WHERE guest=$1 AND NOT ended", guest))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                if (reader.GetGuid(1) != host) throw new GameError(409, "already-visiting");
                return new { id = reader.GetGuid(0), host, guest, connectWithinSeconds = reader.GetInt32(2), status = "reserved" };
            }
        }
        await using (var command = Cmd(connection, transaction,
            "SELECT count(*) FROM visits WHERE host=$1 AND NOT ended AND expires>now()", host))
            if ((long)(await command.ExecuteScalarAsync())! >= GuestLimit)
                throw new GameError(409, "town-full");

        Guid id = Guid.NewGuid();
        await using (var command = Cmd(connection, transaction, """
            INSERT INTO visits(id,host,guest,expires)
            VALUES($1,$2,$3,now()+make_interval(secs => $4))
            """, id, host, guest, (double)ReservationSeconds))
            await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new { id, host, guest, connectWithinSeconds = ReservationSeconds, status = "reserved" };
    }

    public async Task<object> HeartbeatVisit(Guid guest, Guid id, DevicePrincipal? device = null)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Guid host;
        await using (var command = Cmd(connection, transaction, "SELECT host FROM visits WHERE id=$1 AND guest=$2", id, guest))
            host = await command.ExecuteScalarAsync() is Guid town ? town : throw new GameError(404, "visit-not-found");
        await LockVisitParticipants(connection, transaction, host, guest);
        if (device is not null) await RequireActiveDevice(connection, transaction, device);
        await RequireOpenGate(connection, transaction, host, guest);
        await using (var command = Cmd(connection, transaction, """
            UPDATE visits SET expires=now()+make_interval(secs => $4)
            WHERE id=$1 AND host=$2 AND guest=$3 AND NOT ended AND expires>now()
            """, id, host, guest, (double)ReconnectSeconds))
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new GameError(409, "visit-ended");
        await transaction.CommitAsync();
        return new { id, reconnectSeconds = ReconnectSeconds };
    }

    public async Task<object> EndVisit(Guid player, Guid id, DevicePrincipal? device = null)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Guid host, guest;
        await using (var command = Cmd(connection, transaction,
            "SELECT host,guest FROM visits WHERE id=$1 AND (host=$2 OR guest=$2)", id, player))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new GameError(404, "visit-not-found");
            host = reader.GetGuid(0);
            guest = reader.GetGuid(1);
        }
        await LockVisitParticipants(connection, transaction, host, guest);
        if (device is not null) await RequireActiveDevice(connection, transaction, device);
        bool ended;
        await using (var command = Cmd(connection, transaction,
            "UPDATE visits SET ended=true WHERE id=$1 AND NOT ended AND expires>now()", id))
            ended = await command.ExecuteNonQueryAsync() == 1;
        if (ended)
        {
            if (player == host)
            {
                await using var command = Cmd(connection, transaction, """
                    INSERT INTO gate_removals(host,guest) SELECT host,$2 FROM gates WHERE host=$1 AND expires>now()
                    ON CONFLICT DO NOTHING
                    """, host, guest);
                await command.ExecuteNonQueryAsync();
            }
            await using var cleanup = Cmd(connection, transaction, "DELETE FROM tents WHERE host=$1 AND guest=$2", host, guest);
            await cleanup.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return new { ok = true };
    }
}
